using System;
using System.IO;

namespace VGMissionJournal.Persistence;

/// <summary>
/// Pure-function helpers for deriving sidecar paths from vanilla save
/// paths. No I/O, no state.
///
/// <para>Vanilla save files are <c>{SaveGame.SavesPath}/{saveName}.save</c>
/// (GZip-compressed JSON); the paired VGMissionJournal sidecar is
/// <c>{same-path}.vgmissionjournal.json</c>. Quarantined corrupt sidecars
/// get a timestamp suffix and live in the same directory so they survive
/// re-loads and the player can forensically inspect them.</para>
/// </summary>
internal static class JournalPathResolver
{
    internal const string Suffix          = ".vgmissionjournal.json";
    internal const string QuarantineInfix = ".vgmissionjournal.corrupt.";

    // One-shot bridge from the pre-rename VGMissionLog days. When the new
    // ".vgmissionjournal.json" sidecar is missing but the legacy
    // ".vgmissionlog.json" exists at the same base path, the file is
    // renamed in place so existing history isn't orphaned on first load
    // of the renamed plugin. Transient — safe to delete in a later release
    // once it's plausibly done its job on every install.
    private const string LegacySuffix = ".vgmissionlog.json";

    /// <summary>Given a vanilla save path, return the paired sidecar path.
    /// Idempotent — passing a path that already has the suffix returns
    /// the same value, so double-invocation is safe.</summary>
    public static string From(string vanillaSavePath)
    {
        if (vanillaSavePath is null) throw new ArgumentNullException(nameof(vanillaSavePath));
        var newPath = vanillaSavePath.EndsWith(Suffix, StringComparison.Ordinal)
            ? vanillaSavePath
            : vanillaSavePath + Suffix;

        // One-shot legacy-filename rename: promote a ".vgmissionlog.json"
        // sidecar to ".vgmissionjournal.json" so history survives the
        // plugin rename. Failures fall through — subsequent read logic
        // will just see a missing file and treat it as a fresh install.
        try
        {
            if (!File.Exists(newPath))
            {
                var baseSave = vanillaSavePath.EndsWith(Suffix, StringComparison.Ordinal)
                    ? vanillaSavePath.Substring(0, vanillaSavePath.Length - Suffix.Length)
                    : vanillaSavePath;
                var legacyPath = baseSave + LegacySuffix;
                if (File.Exists(legacyPath))
                {
                    File.Move(legacyPath, newPath);
                }
            }
        }
        catch (IOException) { /* fall through */ }
        catch (UnauthorizedAccessException) { /* fall through */ }

        return newPath;
    }

    /// <summary>True iff the path is a live (non-quarantine) VGMissionJournal
    /// sidecar — used by the sweeper to skip <c>.corrupt.</c> files.</summary>
    public static bool IsSidecar(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        return path.EndsWith(Suffix, StringComparison.Ordinal)
            && !path.Contains(QuarantineInfix);
    }

    /// <summary>Recover the vanilla save path from a sidecar path. Returns
    /// the input unchanged if the suffix isn't present (defensive; callers
    /// usually gate with <see cref="IsSidecar"/> first).</summary>
    public static string BaseSavePathFrom(string sidecarPath)
    {
        if (sidecarPath is null) throw new ArgumentNullException(nameof(sidecarPath));
        return sidecarPath.EndsWith(Suffix, StringComparison.Ordinal)
            ? sidecarPath.Substring(0, sidecarPath.Length - Suffix.Length)
            : sidecarPath;
    }

    /// <summary>Quarantine filename for a corrupt sidecar. Timestamp (UTC)
    /// is injected before the <c>.json</c> suffix so files sort naturally
    /// in the save directory listing.</summary>
    public static string QuarantineName(string sidecarPath, DateTime utcNow)
    {
        if (sidecarPath is null) throw new ArgumentNullException(nameof(sidecarPath));
        var stamp = utcNow.ToString("yyyyMMddHHmmss");
        var withoutSuffix = sidecarPath.EndsWith(Suffix, StringComparison.Ordinal)
            ? sidecarPath.Substring(0, sidecarPath.Length - Suffix.Length)
            : sidecarPath;
        return $"{withoutSuffix}{QuarantineInfix}{stamp}.json";
    }
}
