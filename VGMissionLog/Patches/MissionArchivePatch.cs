using System;
using BepInEx.Logging;
using HarmonyLib;
using Source.Player;
using VGMissionLog.Logging;

namespace VGMissionLog.Patches;

/// <summary>
/// Postfix on <see cref="GamePlayer.ArchiveMission(string, bool)"/>
/// (decomp line 33875). Backstop for <see cref="MissionCompletePatch"/>:
/// if no Completed event exists for this storyId in the log's recent
/// history, synthesize one from the most recent prior event.
///
/// <para>Normal path (ML-T4a scout): CompleteMission → ClaimRewards →
/// RemoveMission(completed:true) → ArchiveMission. By the time this
/// patch fires, <see cref="MissionCompletePatch"/> has already emitted
/// a Completed event for the same storyId and the dedup check here
/// short-circuits. The backstop only engages for unusual paths — dev
/// tutorial-skip (decomp line 95144 calls ArchiveMission directly), a
/// swallowed exception in our own CompleteMission postfix, etc.</para>
///
/// <para>Dedup policy: check the most recent event for this storyId in
/// the log. If it's already Completed, skip. Otherwise clone its
/// snapshot fields into a synthesized Completed event via
/// <see cref="ActivityEventBuilder.BuildSynthesizedCompleted"/>. If the
/// log has no prior event for this storyId (e.g. the storyId was never
/// accepted through our hooks), the backstop silently no-ops — there's
/// nothing to clone from and emitting an empty-shaped event wouldn't
/// help consumers.</para>
///
/// <para>The <c>allowDuplicate</c> arg is not a success/failure gate
/// (verified in scout memo) — it just controls whether vanilla adds a
/// duplicate storyId to its archive. We don't branch on it.</para>
/// </summary>
[HarmonyPatch(typeof(GamePlayer), nameof(GamePlayer.ArchiveMission), new[] { typeof(string), typeof(bool) })]
internal static class MissionArchivePatch
{
    internal static ActivityEventBuilder Builder = null!;
    internal static ActivityLog          Log     = null!;
    internal static ManualLogSource      BepLog  = null!;

    [HarmonyPostfix]
    private static void Postfix(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        try
        {
            var existing = Log.GetEventsForStoryId(id);
            if (existing.Count == 0) return;                              // nothing to clone

            var latest = existing[existing.Count - 1];
            if (latest.Type == ActivityEventType.Completed) return;        // dedup: already emitted

            var synth = Builder.BuildSynthesizedCompleted(latest);
            Log.Append(synth);
        }
        catch (Exception e)
        {
            BepLog.LogWarning($"MissionArchivePatch swallowed (storyId='{id}'): {e}");
        }
    }
}
