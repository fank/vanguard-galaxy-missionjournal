using Source.MissionSystem;
using VGMissionLog.Logging;

namespace VGMissionLog.Classification;

/// <summary>
/// Classifies a vanilla <see cref="Mission"/> into a
/// <see cref="MissionType"/> category. Dispatch:
/// <list type="number">
///   <item>Exact subclass match → <c>Bounty</c> / <c>Patrol</c> / <c>Industry</c>.</item>
///   <item>Non-empty <c>storyId</c> with a recognised third-party prefix →
///         <c>ThirdParty(prefix)</c> (e.g. <c>vganima</c>).</item>
///   <item>Non-empty <c>storyId</c> without a third-party prefix →
///         <c>Story</c> (vanilla-registered story arcs).</item>
///   <item>Empty <c>storyId</c> → <c>Generic</c> (mission-board-generated
///         bounty/patrol/industry caught by step 1; anything else is a
///         parametric mission with no story identity).</item>
/// </list>
///
/// Divergence from <c>docs/03-architecture.md</c>: the doc's
/// <c>StoryMission sm =&gt; ClassifyStory(sm)</c> pattern match is
/// incorrect — vanilla's <see cref="StoryMission"/> (decomp line 37657)
/// is a static registrar/factory, <em>not</em> a <c>Mission</c> subclass.
/// At runtime every mission is <c>Mission</c> / <c>BountyMission</c> /
/// <c>PatrolMission</c> / <c>IndustryMission</c>; story-vs-generic-vs-
/// thirdparty discrimination flows through the <c>storyId</c> field on
/// the base class (decomp line 36163).
/// </summary>
internal static class MissionClassifier
{
    public static MissionType Classify(Mission mission)
    {
        return mission switch
        {
            BountyMission   => MissionType.Bounty,
            PatrolMission   => MissionType.Patrol,
            IndustryMission => MissionType.Industry,
            _               => ClassifyFromStoryId(mission),
        };
    }

    private static MissionType ClassifyFromStoryId(Mission mission)
    {
        var storyId = mission.storyId;
        if (string.IsNullOrEmpty(storyId)) return MissionType.Generic;

        var prefix = StoryIdPrefixMap.ExtractPrefix(storyId);
        return prefix is null
            ? MissionType.Story
            : MissionType.ThirdParty(prefix);
    }

    /// <summary>Raw vanilla subclass name for the <c>missionSubclass</c>
    /// event field — captured before deallocation so the event remains
    /// re-classifiable later if the heuristic grows.</summary>
    public static string SubclassName(Mission mission) =>
        mission.GetType().Name;
}
