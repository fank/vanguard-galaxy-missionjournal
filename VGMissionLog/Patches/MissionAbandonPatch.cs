using System;
using BepInEx.Logging;
using HarmonyLib;
using Source.MissionSystem;
using Source.Player;
using VGMissionLog.Logging;

namespace VGMissionLog.Patches;

/// <summary>
/// Postfix on <see cref="GamePlayer.RemoveMission(Mission, bool)"/>
/// (decomp line 33883). Gated on <c>completed == false</c> — emits a
/// <see cref="ActivityEventType.Abandoned"/> event only when the player
/// drops a mission voluntarily.
///
/// <para>Scout finding (ML-T4a memo): <c>RemoveMission</c> is called
/// from two paths — <c>completed:true</c> by <c>ClaimRewards</c> after
/// a successful completion (our <see cref="MissionCompletePatch"/> has
/// already emitted Completed), and <c>completed:false</c> by
/// player-initiated abandon. Without the gate we'd double-emit for
/// every completion; the gate makes this purely the abandon signal.</para>
/// </summary>
[HarmonyPatch(typeof(GamePlayer), nameof(GamePlayer.RemoveMission), new[] { typeof(Mission), typeof(bool) })]
internal static class MissionAbandonPatch
{
    internal static ActivityEventBuilder Builder = null!;
    internal static ActivityLog          Log     = null!;
    internal static ManualLogSource      BepLog  = null!;

    [HarmonyPostfix]
    private static void Postfix(Mission mission, bool completed)
    {
        if (mission is null) return;
        if (completed) return;  // the "completed:true" path is covered by MissionCompletePatch
        try
        {
            var evt = Builder.Build(mission, ActivityEventType.Abandoned);
            Log.Append(evt);
        }
        catch (Exception e)
        {
            BepLog.LogWarning($"MissionAbandonPatch swallowed: {e}");
        }
    }
}
