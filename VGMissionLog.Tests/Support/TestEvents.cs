using VGMissionLog.Logging;

namespace VGMissionLog.Tests.Support;

/// <summary>
/// Test-only builder for <see cref="ActivityEvent"/>. The record's positional
/// ctor is unwieldy in test bodies; <see cref="Baseline"/> returns a
/// fully-populated default and tests layer <c>with</c> expressions on top
/// to vary only the fields under test.
/// </summary>
internal static class TestEvents
{
    public static ActivityEvent Baseline(
        string eventId    = "evt-baseline",
        string storyId    = "story-1",
        double gameSeconds = 0.0,
        ActivityEventType type = ActivityEventType.Accepted,
        string missionSubclass = "Mission",
        string? sourceSystemId = null,
        string? sourceFaction  = null) =>
        new(
            EventId:               eventId,
            Type:                  type,
            GameSeconds:           gameSeconds,
            RealUtc:               "2026-01-01T00:00:00Z",
            StoryId:               storyId,
            MissionName:           "Test Mission",
            MissionSubclass:       missionSubclass,
            MissionLevel:          1,
            Outcome:               null,
            SourceStationId:       null,
            SourceStationName:     null,
            SourceSystemId:        sourceSystemId,
            SourceSystemName:      null,
            SourceSectorId:        null,
            SourceSectorName:      null,
            SourceFaction:         sourceFaction,
            TargetStationId:       null,
            TargetStationName:     null,
            TargetSystemId:        null,
            RewardsCredits:        null,
            RewardsExperience:     null,
            RewardsReputation:     null,
            PlayerLevel:           1,
            PlayerShipName:        null,
            PlayerShipLevel:       null,
            PlayerCurrentSystemId: null,
            Steps:                 null);
}
