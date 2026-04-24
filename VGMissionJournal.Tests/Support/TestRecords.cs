using System;
using System.Collections.Generic;
using VGMissionJournal.Logging;

namespace VGMissionJournal.Tests.Support;

/// <summary>
/// Construction helper for <see cref="MissionRecord"/> instances in tests.
/// Replaces the v1 <c>TestEvents</c> helper. Defaults are chosen so a call
/// like <c>TestRecords.Record()</c> yields a minimal well-formed active
/// record (single Accepted timeline entry at t=0).
/// </summary>
internal static class TestRecords
{
    public static MissionRecord Record(
        string instanceId       = "inst-baseline",
        string storyId          = "story-1",
        double acceptedAt       = 0.0,
        TimelineState? terminal = null,
        double terminalAt       = 0.0,
        string? missionName     = "Test Mission",
        string subclass         = "Mission",
        int missionLevel        = 1,
        string? sourceStationId = null,
        string? sourceSystemId  = null,
        string? sourceFaction   = null,
        IReadOnlyList<MissionStepDefinition>? steps = null,
        IReadOnlyList<MissionRewardSnapshot>? rewards = null)
    {
        var tl = new List<TimelineEntry>
        {
            new(TimelineState.Accepted, acceptedAt, "2026-01-01T00:00:00.0000000Z"),
        };
        if (terminal.HasValue)
            tl.Add(new TimelineEntry(terminal.Value, terminalAt, "2026-01-01T00:00:10.0000000Z"));

        return new MissionRecord(
            StoryId:               storyId,
            MissionInstanceId:     instanceId,
            MissionName:           missionName,
            MissionSubclass:       subclass,
            MissionLevel:          missionLevel,
            SourceStationId:       sourceStationId,
            SourceStationName:     null,
            SourceSystemId:        sourceSystemId,
            SourceSystemName:      null,
            SourceSectorId:        null,
            SourceSectorName:      null,
            SourceFaction:         sourceFaction,
            TargetStationId:       null,
            TargetStationName:     null,
            TargetSystemId:        null,
            PlayerLevel:           1,
            PlayerShipName:        null,
            PlayerShipLevel:       null,
            PlayerCurrentSystemId: null,
            Steps:                 steps   ?? Array.Empty<MissionStepDefinition>(),
            Rewards:               rewards ?? Array.Empty<MissionRewardSnapshot>(),
            Timeline:              tl);
    }
}
