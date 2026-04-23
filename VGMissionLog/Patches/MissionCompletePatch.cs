using System;
using System.Collections.Generic;
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
/// iteration; <c>mission.rewards</c> is still populated.</para>
///
/// <para><b>Archive-race dedup (Prefix).</b> The call chain is
/// <c>CompleteMission → ClaimRewards → RemoveMission(completed:true) →
/// ArchiveMission</c>, and Harmony runs postfixes on return — so
/// <see cref="MissionArchivePatch"/>'s postfix fires *before* this
/// method's postfix. Without coordination, Archive's backstop sees "no
/// Completed event for storyId yet" and synthesizes a duplicate.
/// We handle that by publishing the storyId of the in-flight completion
/// via a <see cref="HashSet{T}"/> (Prefix adds, Postfix removes), and
/// the archive patch skips ids currently in the set. A <c>HashSet</c>
/// rather than a single flag because <c>StoryMission</c>/<c>MissionFollowUp</c>
/// reward handlers can accept (and plausibly complete) chained missions
/// inside the outer <c>CompleteMission</c> call, so re-entrancy is
/// possible.</para>
/// </summary>
[HarmonyPatch(typeof(GamePlayer), nameof(GamePlayer.CompleteMission), new[] { typeof(Mission), typeof(bool) })]
internal static class MissionCompletePatch
{
    internal static ActivityEventBuilder Builder = null!;
    internal static ActivityLog          Log     = null!;
    internal static ManualLogSource      BepLog  = null!;

    // StoryIds currently in-flight through CompleteMission. Read by
    // MissionArchivePatch to suppress its backstop synth when the real
    // Complete event is about to be emitted by our own Postfix.
    internal static readonly HashSet<string> InFlightStoryIds = new(StringComparer.Ordinal);

    [HarmonyPrefix]
    private static void Prefix(Mission m)
    {
        if (m is null) return;
        var id = m.storyId;
        if (!string.IsNullOrEmpty(id)) InFlightStoryIds.Add(id);
    }

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
        finally
        {
            var id = m.storyId;
            if (!string.IsNullOrEmpty(id)) InFlightStoryIds.Remove(id);
        }
    }
}
