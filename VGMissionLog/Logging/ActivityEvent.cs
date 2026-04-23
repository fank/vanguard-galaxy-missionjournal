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
/// </summary>
public sealed record ActivityEvent(
    string EventId,
    ActivityEventType Type,
    double GameSeconds,
    string RealUtc,
    string StoryId,
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
    int PlayerLevel,
    string? PlayerShipName,
    int? PlayerShipLevel,
    string? PlayerCurrentSystemId,
    IReadOnlyList<MissionStepSnapshot>? Steps);
