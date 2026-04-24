using System;
using BepInEx.Logging;
using HarmonyLib;
using Source.MissionSystem;
using Source.Player;
using VGMissionJournal.Logging;

namespace VGMissionJournal.Patches;

/// <summary>
/// Postfix on <see cref="GamePlayer.AddMissionWithLog(Mission)"/>
/// (decomp line 34880). Creates a <see cref="MissionRecord"/> with a
/// single <see cref="TimelineState.Accepted"/> timeline entry whenever
/// vanilla registers a mission as "in progress" for the player.
///
/// <para>Vanilla also has a string-overload of
/// <c>AddMissionWithLog(string)</c> at line 34911 that resolves a
/// <c>StoryMission</c> template and dispatches through the
/// <c>Mission</c>-overload we patch here — so this single hook covers
/// both acceptance paths.</para>
///
/// <para>Exception safety per spec R5.2: the postfix body is wrapped in
/// try/catch; any failure warn-logs and is swallowed.</para>
/// </summary>
[HarmonyPatch(typeof(GamePlayer), nameof(GamePlayer.AddMissionWithLog), new[] { typeof(Mission) })]
internal static class MissionAcceptPatch
{
    internal static MissionRecordBuilder Builder = null!;
    internal static MissionStore         Store   = null!;
    internal static ManualLogSource      BepLog  = null!;

    [HarmonyPostfix]
    private static void Postfix(Mission mission)
    {
        if (mission is null) return;
        try
        {
            var record = Builder.CreateFromAccept(mission);
            Store.Upsert(record);
        }
        catch (Exception e)
        {
            BepLog.LogWarning($"MissionAcceptPatch swallowed: {e}");
        }
    }
}
