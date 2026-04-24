using System;
using BepInEx.Logging;
using HarmonyLib;
using Source.Player;
using VGMissionJournal.Logging;

namespace VGMissionJournal.Patches;

/// <summary>
/// Postfix on <see cref="GamePlayer.ArchiveMission(string, bool)"/>
/// (decomp line 33875). Backstop for <see cref="MissionCompletePatch"/>:
/// if there's an active mission record with this storyId that hasn't
/// received a terminal transition, append a
/// <see cref="TimelineState.Completed"/> entry.
///
/// <para>Dedup via <see cref="MissionCompletePatch.InFlightStoryIds"/>:
/// when CompleteMission is in flight, skip the synth so we don't
/// double-append.</para>
/// </summary>
[HarmonyPatch(typeof(GamePlayer), nameof(GamePlayer.ArchiveMission), new[] { typeof(string), typeof(bool) })]
internal static class MissionArchivePatch
{
    internal static MissionRecordBuilder Builder = null!;
    internal static MissionStore         Store   = null!;
    internal static ManualLogSource      BepLog  = null!;

    [HarmonyPostfix]
    private static void Postfix(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (MissionCompletePatch.InFlightStoryIds.Contains(id)) return;
        try
        {
            // Find the last active record carrying this storyId.
            MissionRecord? latestActive = null;
            foreach (var r in Store.AllMissions)
            {
                if (string.Equals(r.StoryId, id, StringComparison.Ordinal) && r.IsActive)
                    latestActive = r;
            }
            if (latestActive is null) return;
            var next = Builder.AppendTransition(latestActive, TimelineState.Completed, mission: null);
            Store.Upsert(next);
        }
        catch (Exception e)
        {
            BepLog.LogWarning($"MissionArchivePatch swallowed (storyId='{id}'): {e}");
        }
    }
}
