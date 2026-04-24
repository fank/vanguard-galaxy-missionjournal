using System;
using BepInEx.Logging;
using HarmonyLib;
using Source.Util;
using VGMissionLog.Logging;
using VGMissionLog.Persistence;

namespace VGMissionLog.Patches;

/// <summary>
/// Prefix on <see cref="SaveGameFile.LoadSaveGame"/> — replaces the
/// in-memory <see cref="MissionStore"/> from its sidecar before vanilla
/// starts applying the save state.
///
/// <para>Error policy per spec R3.4:
/// <see cref="LogReadStatus.MissingFile"/> → empty store, silent.
/// <see cref="LogReadStatus.Corrupted"/> /
/// <see cref="LogReadStatus.UnsupportedVersion"/> → empty store, warn-
/// log quarantine path. In all cases vanilla's load proceeds; we never
/// rethrow.</para>
/// </summary>
[HarmonyPatch(typeof(SaveGameFile), nameof(SaveGameFile.LoadSaveGame))]
internal static class SaveLoadPatch
{
    internal static MissionStore     Store  = null!;
    internal static LogIO            IO     = null!;
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
            var result   = IO.Read(sidecar);
            LastKnownSavePath = savePath;

            switch (result.Status)
            {
                case LogReadStatus.Loaded:
                    Store.LoadFrom(result.Schema!.Missions);
                    BepLog.LogInfo($"Loaded {result.Schema.Missions.Length} mission(s) from {sidecar}");
                    break;
                case LogReadStatus.MissingFile:
                    Store.LoadFrom(Array.Empty<MissionRecord>());
                    break;
                case LogReadStatus.Corrupted:
                    Store.LoadFrom(Array.Empty<MissionRecord>());
                    BepLog.LogWarning($"Corrupt sidecar quarantined to {result.QuarantinedTo}; starting empty store");
                    break;
                case LogReadStatus.UnsupportedVersion:
                    Store.LoadFrom(Array.Empty<MissionRecord>());
                    BepLog.LogWarning($"Unsupported sidecar version quarantined to {result.QuarantinedTo}; starting empty store");
                    break;
            }
        }
        catch (Exception e)
        {
            BepLog.LogWarning($"SaveLoadPatch swallowed: {e}");
            try { Store.LoadFrom(Array.Empty<MissionRecord>()); } catch { }
        }
    }
}
