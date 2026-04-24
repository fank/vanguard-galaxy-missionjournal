using System;
using System.IO;
using System.Linq;
using BepInEx.Logging;
using HarmonyLib;
using Source.Util;
using VGMissionJournal.Logging;
using VGMissionJournal.Persistence;

namespace VGMissionJournal.Patches;

/// <summary>
/// Postfix on <see cref="SaveGame.Store"/> (decomp line 22163) — flushes
/// the in-memory <see cref="MissionStore"/> to its sidecar after vanilla's
/// own save succeeds.
///
/// <para><see cref="LastKnownSavePath"/> is tracked for the
/// <c>Application.quitting</c> safety-net flush in Plugin.Awake.</para>
/// </summary>
[HarmonyPatch(typeof(SaveGame), nameof(SaveGame.Store))]
internal static class SaveWritePatch
{
    internal static MissionStore     Store   = null!;
    internal static JournalIO            IO      = null!;
    internal static ManualLogSource  BepLog  = null!;

    internal static string? LastKnownSavePath { get; private set; }

    [HarmonyPostfix]
    private static void Postfix(string saveName)
    {
        if (string.IsNullOrEmpty(saveName)) return;
        try
        {
            var savePath = Path.Combine(SaveGame.SavesPath, saveName + ".save");
            var sidecar  = JournalPathResolver.From(savePath);
            var schema   = new JournalSchema(
                JournalSchema.CurrentVersion,
                Store.AllMissions.ToArray());
            IO.Write(sidecar, schema);
            LastKnownSavePath = savePath;
            BepLog.LogInfo($"Flushed {schema.Missions.Length} mission(s) to {sidecar}");
        }
        catch (Exception e)
        {
            BepLog.LogWarning($"SaveWritePatch swallowed (saveName='{saveName}'): {e}");
        }
    }
}
