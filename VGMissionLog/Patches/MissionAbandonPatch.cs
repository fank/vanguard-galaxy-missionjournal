using System;
using BepInEx.Logging;
using HarmonyLib;
using Source.MissionSystem;
using Source.Player;
using VGMissionLog.Logging;

namespace VGMissionLog.Patches;

/// <summary>
/// Postfix on <see cref="GamePlayer.RemoveMission(Mission, bool)"/>
/// (decomp line 33883). Gated on <c>completed == false</c> — appends a
/// <see cref="TimelineState.Abandoned"/> transition only when the player
/// drops a mission voluntarily.
/// </summary>
[HarmonyPatch(typeof(GamePlayer), nameof(GamePlayer.RemoveMission), new[] { typeof(Mission), typeof(bool) })]
internal static class MissionAbandonPatch
{
    internal static MissionRecordBuilder Builder = null!;
    internal static MissionStore         Store   = null!;
    internal static ManualLogSource      BepLog  = null!;

    [HarmonyPostfix]
    private static void Postfix(Mission mission, bool completed)
    {
        if (mission is null) return;
        if (completed) return;  // the "completed:true" path is covered by MissionCompletePatch
        try
        {
            var key      = Builder.GetInstanceId(mission);
            var existing = Store.GetByInstanceId(key);
            if (existing is null) return;
            var next = Builder.AppendTransition(existing, TimelineState.Abandoned, mission);
            Store.Upsert(next);
        }
        catch (Exception e)
        {
            BepLog.LogWarning($"MissionAbandonPatch swallowed: {e}");
        }
    }
}
