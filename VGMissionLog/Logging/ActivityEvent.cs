using System.Collections.Generic;

namespace VGMissionLog.Logging;

/// <summary>
/// One entry in the mission activity log. Snapshots the state of a mission
/// at a single lifecycle transition — see spec R1 for the full capture
/// contract. All fields are primitive or nullable-primitive so the sidecar
/// JSON stays <c>TypeNameHandling.None</c>-safe.
///
/// Display-name fields (Mission/Station/System/Sector names) are snapshots:
/// vanilla may rename or destroy a station later, but the log must render
/// historical events as they were (spec R1.3).
///
/// <para><b>No classification.</b> We record what the game gives us —
/// <see cref="MissionSubclass"/> is the raw <c>mission.GetType().Name</c>
/// ("BountyMission", "PatrolMission", "IndustryMission", "Mission", …).
/// Consumers that want to bucket missions into categories do so themselves
/// from this raw string; the monitor does not editorialize.</para>
///
/// <para><b>Linking accepted → terminal events.</b> Vanilla only populates
/// <see cref="StoryId"/> on authored story missions (Tutorial, Puppeteers),
/// leaving it empty for the generator-produced missions that make up most
/// gameplay. <see cref="MissionInstanceId"/> is a session-local GUID
/// synthesized per <c>Mission</c> instance so consumers can correlate an
/// Accepted event with its Completed/Failed/Abandoned counterpart for any
/// mission, not just story ones. <b>The id does not survive save/load</b>
/// — vanilla rebuilds mission instances on load, so a mission accepted in
/// session A and completed in session B will have different ids on each
/// event.</para>
/// </summary>
public sealed record ActivityEvent(
    string EventId,
    ActivityEventType Type,
    double GameSeconds,
    string RealUtc,
    string StoryId,
    string MissionInstanceId,
    string? MissionName,
    string MissionSubclass,
    int MissionLevel,
    Outcome? Outcome,
    string? SourceStationId,
    string? SourceStationName,
    string? SourceSystemId,
    string? SourceSystemName,
    string? SourceSectorId,
    string? SourceSectorName,
    string? SourceFaction,
    string? TargetStationId,
    string? TargetStationName,
    string? TargetSystemId,
    long? RewardsCredits,
    long? RewardsExperience,
    IReadOnlyList<RepReward>? RewardsReputation,
    IReadOnlyList<MissionRewardSnapshot>? Rewards,
    int PlayerLevel,
    string? PlayerShipName,
    int? PlayerShipLevel,
    string? PlayerCurrentSystemId,
    IReadOnlyList<MissionStepSnapshot>? Steps);
