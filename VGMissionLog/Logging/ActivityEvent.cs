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
/// </summary>
public sealed record ActivityEvent(
    string EventId,
    ActivityEventType Type,
    double GameSeconds,
    string RealUtc,
    string StoryId,
    string? MissionName,
    MissionType MissionType,
    string MissionSubclass,
    int MissionLevel,
    ActivityArchetype? Archetype,
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
    FacilityOrigin? FacilityOrigin,
    long? RewardsCredits,
    long? RewardsExperience,
    IReadOnlyList<RepReward>? RewardsReputation,
    int PlayerLevel,
    string? PlayerShipName,
    int? PlayerShipLevel,
    string? PlayerCurrentSystemId);
