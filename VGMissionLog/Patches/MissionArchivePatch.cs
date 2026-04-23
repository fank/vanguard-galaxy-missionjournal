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
/// <para>Call chain and timing: <c>CompleteMission → ClaimRewards →
/// RemoveMission(completed:true) → ArchiveMission</c>. Harmony postfixes
/// run when their patched method returns, so Archive's postfix fires
/// *before* CompleteMission's — that means a naive "log has no
/// Completed for this storyId" check would always synth a duplicate for
/// real completions. We coordinate with
/// <see cref="MissionCompletePatch.InFlightStoryIds"/>: the Complete
/// prefix adds the storyId, this postfix skips ids currently in that
/// set, and the Complete postfix removes it afterwards. Only storyIds
/// not in the set (dev tutorial-skip calling <c>ArchiveMission</c>
/// directly, or a swallowed throw in our own Complete prefix) fall
/// through to the backstop.</para>
///
/// <para>Dedup policy: check the most recent event for this storyId in
/// the log. If it's already Completed, skip. Otherwise clone its
/// snapshot fields into a synthesized Completed event via
/// <see cref="ActivityEventBuilder.BuildSynthesizedCompleted"/>. If the
/// log has no prior event for this storyId (e.g. the storyId was never
/// accepted through our hooks), the backstop silently no-ops — there's
/// nothing to clone from.</para>
///
/// <para>The <c>allowDuplicate</c> arg is not a success/failure gate —
/// it just controls whether vanilla adds a duplicate storyId to its
/// archive. We don't branch on it.</para>
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
        // MissionCompletePatch is mid-flight for this storyId; its Postfix
        // will emit the real Completed event momentarily. Don't synth.
        if (MissionCompletePatch.InFlightStoryIds.Contains(id)) return;
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
