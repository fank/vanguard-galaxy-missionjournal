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
/// (decomp line 33932). Appends a <see cref="TimelineState.Completed"/>
/// transition to the matching <see cref="MissionRecord"/>.
///
/// <para><b>Archive-race dedup (Prefix).</b> The call chain is
/// <c>CompleteMission → ClaimRewards → RemoveMission(completed:true) →
/// ArchiveMission</c>, and Harmony runs postfixes on return — so
/// <see cref="MissionArchivePatch"/>'s postfix fires *before* this
/// method's postfix. Without coordination, Archive's backstop sees "no
/// Completed transition for storyId yet" and synthesizes a duplicate.
/// We handle that by publishing the storyId of the in-flight completion
/// via a <see cref="HashSet{T}"/> (Prefix adds, Postfix removes), and
/// the archive patch skips ids currently in the set.</para>
/// </summary>
[HarmonyPatch(typeof(GamePlayer), nameof(GamePlayer.CompleteMission), new[] { typeof(Mission), typeof(bool) })]
internal static class MissionCompletePatch
{
    internal static MissionRecordBuilder Builder = null!;
    internal static MissionStore         Store   = null!;
    internal static ManualLogSource      BepLog  = null!;

    // StoryIds currently in-flight through CompleteMission. Read by
    // MissionArchivePatch to suppress its backstop synth when the real
    // Complete transition is about to be appended by our own Postfix.
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
            var key      = Builder.GetInstanceId(m);
            var existing = Store.GetByInstanceId(key);
            if (existing is null) return;  // never saw accept — unusual, drop
            var next = Builder.AppendTransition(existing, TimelineState.Completed, m);
            Store.Upsert(next);
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
