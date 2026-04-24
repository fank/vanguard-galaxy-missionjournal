using System.Collections.Generic;
using VGMissionLog.Logging;
using Xunit;

namespace VGMissionLog.Tests.Logging;

public class MissionRecordTests
{
    private static MissionRecord Sample(params TimelineEntry[] timeline) =>
        new(
            StoryId:               "story-1",
            MissionInstanceId:     "inst-1",
            MissionName:           "Hunt the Crimson Fang",
            MissionSubclass:       "BountyMission",
            MissionLevel:          4,
            SourceStationId:       null, SourceStationName: null,
            SourceSystemId:        "sys-zoran", SourceSystemName: "Zoran",
            SourceSectorId:        null, SourceSectorName: null,
            SourceFaction:         "BountyGuild",
            TargetStationId:       null, TargetStationName: null, TargetSystemId: null,
            PlayerLevel:           1, PlayerShipName: null, PlayerShipLevel: null,
            PlayerCurrentSystemId: null,
            Steps:                 System.Array.Empty<MissionStepDefinition>(),
            Rewards:               System.Array.Empty<MissionRewardSnapshot>(),
            Timeline:              timeline);

    [Fact]
    public void IsActive_TrueWhenOnlyAccepted()
    {
        var r = Sample(new TimelineEntry(TimelineState.Accepted, 100.0, null));
        Assert.True(r.IsActive);
        Assert.Null(r.Outcome);
        Assert.Null(r.TerminalAtGameSeconds);
    }

    [Fact]
    public void IsActive_FalseWhenTerminalPresent()
    {
        var r = Sample(
            new TimelineEntry(TimelineState.Accepted,  100.0, null),
            new TimelineEntry(TimelineState.Completed, 420.0, null));
        Assert.False(r.IsActive);
        Assert.Equal(Outcome.Completed, r.Outcome);
        Assert.Equal(420.0, r.TerminalAtGameSeconds);
    }

    [Fact]
    public void AcceptedAtGameSeconds_ReadsFromFirstTimelineEntry()
    {
        var r = Sample(new TimelineEntry(TimelineState.Accepted, 100.0, null));
        Assert.Equal(100.0, r.AcceptedAtGameSeconds);
    }

    [Fact]
    public void AgeSeconds_UsesTerminalIfPresent_ElseNow()
    {
        var active = Sample(new TimelineEntry(TimelineState.Accepted, 100.0, null));
        Assert.Equal(400.0, active.AgeSeconds(nowGameSeconds: 500.0));

        var done = Sample(
            new TimelineEntry(TimelineState.Accepted,  100.0, null),
            new TimelineEntry(TimelineState.Completed, 420.0, null));
        Assert.Equal(320.0, done.AgeSeconds(nowGameSeconds: 9999.0));  // now ignored when terminal exists
    }

    [Theory]
    [InlineData(TimelineState.Completed, Outcome.Completed)]
    [InlineData(TimelineState.Failed,    Outcome.Failed)]
    [InlineData(TimelineState.Abandoned, Outcome.Abandoned)]
    public void Outcome_MapsFromTerminalState(TimelineState state, Outcome expected)
    {
        var r = Sample(
            new TimelineEntry(TimelineState.Accepted, 0.0, null),
            new TimelineEntry(state,                  1.0, null));
        Assert.Equal(expected, r.Outcome);
    }

    [Fact]
    public void EmptyTimeline_IsActiveIsTrue_ButAcceptedAtThrows()
    {
        // MissionRecordBuilder guarantees a non-empty timeline; this test
        // pins the current behaviour of the record if that invariant ever
        // slips: IsActive/Outcome/TerminalAt are "safe" nulls/true, but
        // AcceptedAtGameSeconds (and therefore AgeSeconds) throws.
        var r = Sample();   // no timeline entries

        Assert.True(r.IsActive);
        Assert.Null(r.Outcome);
        Assert.Null(r.TerminalAtGameSeconds);
        Assert.Throws<System.InvalidOperationException>(() => r.AcceptedAtGameSeconds);
        Assert.Throws<System.InvalidOperationException>(() => r.AgeSeconds(nowGameSeconds: 1000.0));
    }

    [Fact]
    public void NonTerminalLastEntry_IsTreatedAsActive()
    {
        // TerminalEntry guards against a non-terminal state in the last
        // slot (it only returns when IsTerminal). A timeline of two
        // Accepted entries (shouldn't happen, but defensively) still
        // reports Active.
        var r = Sample(
            new TimelineEntry(TimelineState.Accepted, 100.0, null),
            new TimelineEntry(TimelineState.Accepted, 150.0, null));   // degenerate

        Assert.True(r.IsActive);
        Assert.Null(r.Outcome);
        Assert.Null(r.TerminalAtGameSeconds);
        Assert.Equal(100.0, r.AcceptedAtGameSeconds);    // still reads position 0
    }
}
