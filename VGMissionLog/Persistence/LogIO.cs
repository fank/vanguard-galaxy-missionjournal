using System;
using System.IO;
using Newtonsoft.Json;

namespace VGMissionLog.Persistence;

internal enum LogReadStatus { Loaded, MissingFile, Corrupted, UnsupportedVersion }

internal sealed record LogReadResult(
    LogReadStatus   Status,
    LogSchema?      Schema,
    string?         QuarantinedTo);

/// <summary>
/// Reads and writes the log sidecar file. Writes are atomic (tmp +
/// rename); reads quarantine corrupt or future-version files so vanilla's
/// load can proceed with an empty log.
///
/// <para>Version acceptance policy: the current
/// <see cref="LogSchema.CurrentVersion"/> loads as-is; anything else
/// quarantines. At MVP (v1) there's no back-compat window — future
/// bumps add an upgrade path here (additive, in-memory restamp when
/// schema.Version == CurrentVersion - 1).</para>
///
/// <para>Exceptions from <see cref="Write"/> are <b>not</b> swallowed
/// here — the caller (<c>SaveWritePatch</c>) is the layer that must
/// catch and warn-log per spec R5.2. Keeping IO thin-and-throwing
/// makes this layer easy to reason about.</para>
/// </summary>
internal sealed class LogIO
{
    private readonly Func<DateTime> _utcNow;

    public LogIO(Func<DateTime> utcNow)
    {
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
    }

    public LogReadResult Read(string sidecarPath)
    {
        if (!File.Exists(sidecarPath))
            return new LogReadResult(LogReadStatus.MissingFile, null, null);

        string raw;
        try { raw = File.ReadAllText(sidecarPath); }
        catch (IOException)
        {
            // Transient IO issue — prefer the empty-log branch over crashing
            // vanilla's load; don't quarantine since nothing's been parsed.
            return new LogReadResult(LogReadStatus.MissingFile, null, null);
        }

        LogSchema? schema;
        try { schema = JsonConvert.DeserializeObject<LogSchema>(raw, LogSchema.SerializerSettings); }
        catch (JsonException) { return Quarantine(sidecarPath, LogReadStatus.Corrupted); }

        if (schema is null) return Quarantine(sidecarPath, LogReadStatus.Corrupted);

        if (schema.Version == LogSchema.CurrentVersion)
            return new LogReadResult(LogReadStatus.Loaded, schema, null);

        // At v1 there's no back-compat window. When we bump to v2, the
        // additive upgrade path lives here: if schema.Version ==
        // CurrentVersion - 1 and the bump is additive, rewrite with
        // `schema with { Version = CurrentVersion }` and return Loaded.
        return Quarantine(sidecarPath, LogReadStatus.UnsupportedVersion);
    }

    public void Write(string sidecarPath, LogSchema schema)
    {
        if (sidecarPath is null) throw new ArgumentNullException(nameof(sidecarPath));
        if (schema is null)      throw new ArgumentNullException(nameof(schema));

        var tmp  = sidecarPath + ".tmp";
        var json = JsonConvert.SerializeObject(schema, LogSchema.SerializerSettings);
        File.WriteAllText(tmp, json);
        if (File.Exists(sidecarPath)) File.Delete(sidecarPath);
        File.Move(tmp, sidecarPath);
    }

    private LogReadResult Quarantine(string sidecarPath, LogReadStatus status)
    {
        var quarantinePath = LogPathResolver.QuarantineName(sidecarPath, _utcNow());
        File.Move(sidecarPath, quarantinePath);
        return new LogReadResult(status, null, quarantinePath);
    }
}
