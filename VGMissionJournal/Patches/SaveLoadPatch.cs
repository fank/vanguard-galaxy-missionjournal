using System;
using BepInEx.Logging;
using HarmonyLib;
using Source.Util;
using VGMissionJournal.Logging;
using VGMissionJournal.Persistence;

namespace VGMissionJournal.Patches;

/// <summary>
/// Prefix on <see cref="SaveGameFile.LoadSaveGame"/> — replaces the
/// in-memory <see cref="MissionStore"/> from its sidecar before vanilla
/// starts applying the save state.
///
/// <para>Error policy per spec R3.4:
/// <see cref="JournalReadStatus.MissingFile"/> → empty store, silent.
/// <see cref="JournalReadStatus.Corrupted"/> /
/// <see cref="JournalReadStatus.UnsupportedVersion"/> → empty store, warn-
/// log quarantine path. In all cases vanilla's load proceeds; we never
/// rethrow.</para>
/// </summary>
[HarmonyPatch(typeof(SaveGameFile), nameof(SaveGameFile.LoadSaveGame))]
internal static class SaveLoadPatch
{
    internal static MissionStore     Store  = null!;
    internal static JournalIO            IO     = null!;
    internal static ManualLogSource  BepLog = null!;

    internal static string? LastKnownSavePath { get; private set; }

    [HarmonyPrefix]
    private static void Prefix(SaveGameFile __instance)
    {
        if (__instance?.File is null) return;
        try
        {
            var savePath = __instance.File.FullName;
            var sidecar  = JournalPathResolver.From(savePath);
            var result   = IO.Read(sidecar);
            LastKnownSavePath = savePath;

            switch (result.Status)
            {
                case JournalReadStatus.Loaded:
                    Store.LoadFrom(result.Schema!.Missions);
                    BepLog.LogInfo($"Loaded {result.Schema.Missions.Length} mission(s) from {sidecar}");
                    break;
                case JournalReadStatus.MissingFile:
                    Store.LoadFrom(Array.Empty<MissionRecord>());
                    break;
                case JournalReadStatus.Corrupted:
                    Store.LoadFrom(Array.Empty<MissionRecord>());
                    BepLog.LogWarning($"Corrupt sidecar quarantined to {result.QuarantinedTo}; starting empty store");
                    break;
                case JournalReadStatus.UnsupportedVersion:
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
