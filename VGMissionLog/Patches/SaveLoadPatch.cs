using System;
using BepInEx.Logging;
using HarmonyLib;
using Source.Util;
using VGMissionLog.Logging;
using VGMissionLog.Persistence;

namespace VGMissionLog.Patches;

/// <summary>
/// Prefix on <see cref="SaveGameFile.LoadSaveGame"/> (decomp line 22283)
/// — replaces the in-memory <see cref="ActivityLog"/> from its sidecar
/// before vanilla starts applying the save state.
///
/// <para>This is the one allowed prefix per spec R5.1 (save-load). The
/// sidecar is populated before any hypothetical post-load consumer
/// queries during vanilla's load body. We're still read-only (no
/// mutation of vanilla state); the prefix is a timing convenience,
/// not a mutation vector.</para>
///
/// <para>Error policy per spec R3.4:
/// <see cref="LogReadStatus.MissingFile"/> → empty log, silent.
/// <see cref="LogReadStatus.Corrupted"/> /
/// <see cref="LogReadStatus.UnsupportedVersion"/> → empty log, warn-
/// log quarantine path. In all cases vanilla's load proceeds; we never
/// rethrow.</para>
/// </summary>
[HarmonyPatch(typeof(SaveGameFile), nameof(SaveGameFile.LoadSaveGame))]
internal static class SaveLoadPatch
{
    internal static ActivityLog      Log    = null!;
    internal static LogIO            Io     = null!;
    internal static ManualLogSource  BepLog = null!;

    internal static string? LastKnownSavePath { get; private set; }

    [HarmonyPrefix]
    private static void Prefix(SaveGameFile __instance)
    {
        if (__instance?.File is null) return;
        try
        {
            var savePath = __instance.File.FullName;
            var sidecar  = LogPathResolver.From(savePath);
            var result   = Io.Read(sidecar);
            LastKnownSavePath = savePath;

            switch (result.Status)
            {
                case LogReadStatus.Loaded:
                    Log.LoadFrom(result.Schema!.Events);
                    BepLog.LogInfo($"Loaded {result.Schema.Events.Length} event(s) from {sidecar}");
                    break;
                case LogReadStatus.MissingFile:
                    Log.LoadFrom(Array.Empty<ActivityEvent>());
                    break;
                case LogReadStatus.Corrupted:
                    Log.LoadFrom(Array.Empty<ActivityEvent>());
                    BepLog.LogWarning($"Corrupt sidecar quarantined to {result.QuarantinedTo}; starting empty log");
                    break;
                case LogReadStatus.UnsupportedVersion:
                    Log.LoadFrom(Array.Empty<ActivityEvent>());
                    BepLog.LogWarning($"Unsupported sidecar version quarantined to {result.QuarantinedTo}; starting empty log");
                    break;
            }
        }
        catch (Exception e)
        {
            BepLog.LogWarning($"SaveLoadPatch swallowed: {e}");
            // Even on unexpected failure, make sure the in-memory log isn't
            // left carrying the previous save's events.
            try { Log.LoadFrom(Array.Empty<ActivityEvent>()); } catch { }
        }
    }
}
