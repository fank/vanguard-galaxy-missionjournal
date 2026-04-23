using System;

namespace VGMissionLog.Persistence;

/// <summary>
/// Pure-function helpers for deriving sidecar paths from vanilla save
/// paths. No I/O, no state.
///
/// <para>Vanilla save files are <c>{SaveGame.SavesPath}/{saveName}.save</c>
/// (GZip-compressed JSON); the paired VGMissionLog sidecar is
/// <c>{same-path}.vgmissionlog.json</c>. Quarantined corrupt sidecars
/// get a timestamp suffix and live in the same directory so they survive
/// re-loads and the player can forensically inspect them.</para>
///
/// <para>Mirrors VGAnima's <c>SidecarPathResolver</c> shape so both mods
/// behave identically in the save directory (paired JSON, same dir,
/// same quarantine pattern).</para>
/// </summary>
internal static class LogPathResolver
{
    internal const string Suffix          = ".vgmissionlog.json";
    internal const string QuarantineInfix = ".vgmissionlog.corrupt.";

    /// <summary>Given a vanilla save path, return the paired sidecar path.
    /// Idempotent — passing a path that already has the suffix returns
    /// the same value, so double-invocation is safe.</summary>
    public static string From(string vanillaSavePath)
    {
        if (vanillaSavePath is null) throw new ArgumentNullException(nameof(vanillaSavePath));
        return vanillaSavePath.EndsWith(Suffix, StringComparison.Ordinal)
            ? vanillaSavePath
            : vanillaSavePath + Suffix;
    }

    /// <summary>True iff the path is a live (non-quarantine) VGMissionLog
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
