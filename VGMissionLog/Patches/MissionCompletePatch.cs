using System;
using BepInEx.Logging;
using HarmonyLib;
using Source.MissionSystem;
using Source.Player;
using VGMissionLog.Logging;

namespace VGMissionLog.Patches;

/// <summary>
/// Postfix on <see cref="GamePlayer.CompleteMission(Mission, bool)"/>
/// (decomp line 33932). Emits a <see cref="ActivityEventType.Completed"/>
/// event and extracts credits / experience / reputation from
/// <c>mission.rewards</c>.
///
/// <para>Reward timing: vanilla's <c>CompleteMission</c> calls
/// <c>m.ClaimRewards(force)</c> first, which iterates the reward list
/// without mutating it (line 36425–36428). Postfix fires after the
/// iteration; <c>mission.rewards</c> is still populated. No prefix
/// needed (contrary to the plan's caveat — verified in the ML-T4a
/// scout memo).</para>
///
/// <para>Call-chain side-effects (see scout memo): CompleteMission →
/// ClaimRewards → RemoveMission(completed:true) → ArchiveMission. Our
/// <see cref="MissionAbandonPatch"/> gates on completed=false and our
/// <see cref="MissionArchivePatch"/> dedups against recent Completed
/// events, so a single player-triggered complete emits exactly one
/// Completed event.</para>
/// </summary>
[HarmonyPatch(typeof(GamePlayer), nameof(GamePlayer.CompleteMission), new[] { typeof(Mission), typeof(bool) })]
internal static class MissionCompletePatch
{
    internal static ActivityEventBuilder Builder = null!;
    internal static ActivityLog          Log     = null!;
    internal static ManualLogSource      BepLog  = null!;

    [HarmonyPostfix]
    private static void Postfix(Mission m)
    {
        if (m is null) return;
        try
        {
            var evt = Builder.Build(m, ActivityEventType.Completed);
            Log.Append(evt);
        }
        catch (Exception e)
        {
            BepLog.LogWarning($"MissionCompletePatch swallowed: {e}");
        }
    }
}
