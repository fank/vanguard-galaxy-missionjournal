using System;
using System.IO;
using Newtonsoft.Json;

namespace VGMissionJournal.Persistence;

internal enum JournalReadStatus { Loaded, MissingFile, Corrupted, UnsupportedVersion }

internal sealed record JournalReadResult(
    JournalReadStatus   Status,
    JournalSchema?      Schema,
    string?         QuarantinedTo);

/// <summary>
/// Reads and writes the log sidecar file. Writes are atomic (tmp +
/// rename); reads quarantine corrupt or future-version files so vanilla's
/// load can proceed with an empty log.
///
/// <para>Version acceptance policy: v3 loads as-is; v1 is routed through
/// <see cref="V1ToV3Migrator"/>; anything else quarantines.</para>
///
/// <para>Exceptions from <see cref="Write"/> are <b>not</b> swallowed
/// here — the caller (<c>SaveWritePatch</c>) is the layer that must
/// catch and warn-log per spec R5.2.</para>
/// </summary>
internal sealed class JournalIO
{
    private readonly Func<DateTime> _utcNow;

    public JournalIO(Func<DateTime> utcNow)
    {
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
    }

    public JournalReadResult Read(string sidecarPath)
    {
        if (!File.Exists(sidecarPath))
            return new JournalReadResult(JournalReadStatus.MissingFile, null, null);

        string raw;
        try { raw = File.ReadAllText(sidecarPath); }
        catch (IOException)
        {
            // Transient IO issue — prefer the empty-log branch over crashing
            // vanilla's load; don't quarantine since nothing's been parsed.
            return new JournalReadResult(JournalReadStatus.MissingFile, null, null);
        }

        // First pass: read just the version.
        int version;
        try
        {
            var probe = JsonConvert.DeserializeObject<VersionProbe>(raw, JournalSchema.SerializerSettings);
            version = probe?.Version ?? 0;
        }
        catch (JsonException) { return Quarantine(sidecarPath, JournalReadStatus.Corrupted); }

        if (version == JournalSchema.CurrentVersion)
        {
            JournalSchema? schema;
            try { schema = JsonConvert.DeserializeObject<JournalSchema>(raw, JournalSchema.SerializerSettings); }
            catch (JsonException) { return Quarantine(sidecarPath, JournalReadStatus.Corrupted); }
            if (schema is null) return Quarantine(sidecarPath, JournalReadStatus.Corrupted);
            return new JournalReadResult(JournalReadStatus.Loaded, schema, null);
        }

        if (version == 1)
        {
            try
            {
                var migrated = V1ToV3Migrator.Migrate(raw);
                return new JournalReadResult(JournalReadStatus.Loaded, migrated, null);
            }
            catch (Exception) { return Quarantine(sidecarPath, JournalReadStatus.Corrupted); }
        }

        return Quarantine(sidecarPath, JournalReadStatus.UnsupportedVersion);
    }

    public void Write(string sidecarPath, JournalSchema schema)
    {
        if (sidecarPath is null) throw new ArgumentNullException(nameof(sidecarPath));
        if (schema is null)      throw new ArgumentNullException(nameof(schema));

        var tmp  = sidecarPath + ".tmp";
        var json = JsonConvert.SerializeObject(schema, JournalSchema.SerializerSettings);
        File.WriteAllText(tmp, json);
        if (File.Exists(sidecarPath)) File.Delete(sidecarPath);
        File.Move(tmp, sidecarPath);
    }

    private JournalReadResult Quarantine(string sidecarPath, JournalReadStatus status)
    {
        var quarantinePath = JournalPathResolver.QuarantineName(sidecarPath, _utcNow());
        File.Move(sidecarPath, quarantinePath);
        return new JournalReadResult(status, null, quarantinePath);
    }

    private sealed class VersionProbe
    {
        public int Version { get; set; }
    }
}
