using System.Collections.Generic;
using System.Linq;
using VGMissionLog.Logging;
using VGMissionLog.Tests.Support;
using Xunit;

namespace VGMissionLog.Tests.Logging;

public class ActivityLogAggregateTests
{
    // --- shared fixtures -----------------------------------------------------

    private static ActivityLog BuildVariedLog()
    {
        var log = new ActivityLog();
        // 3 bounty (Zoran/BountyGuild — 1 accepted, 1 completed, 1 failed)
        log.Append(TestEvents.Baseline(eventId: "b1", gameSeconds: 10,
            type: ActivityEventType.Accepted, missionType: MissionType.Bounty,
            sourceSystemId: "sys-zoran",  sourceFaction: "BountyGuild"));
        log.Append(TestEvents.Baseline(eventId: "b2", gameSeconds: 20,
            type: ActivityEventType.Completed, missionType: MissionType.Bounty,
            sourceSystemId: "sys-zoran",  sourceFaction: "BountyGuild")
            with { Outcome = Outcome.Completed });
        log.Append(TestEvents.Baseline(eventId: "b3", gameSeconds: 30,
            type: ActivityEventType.Failed, missionType: MissionType.Bounty,
            sourceSystemId: "sys-zoran",  sourceFaction: "BountyGuild")
            with { Outcome = Outcome.Failed });
        // 2 patrol (Helion/Police — accepted + abandoned)
        log.Append(TestEvents.Baseline(eventId: "p1", gameSeconds: 40,
            type: ActivityEventType.Accepted, missionType: MissionType.Patrol,
            sourceSystemId: "sys-helion", sourceFaction: "Police"));
        log.Append(TestEvents.Baseline(eventId: "p2", gameSeconds: 50,
            type: ActivityEventType.Abandoned, missionType: MissionType.Patrol,
            sourceSystemId: "sys-helion", sourceFaction: "Police")
            with { Outcome = Outcome.Abandoned });
        // 1 vganima third-party (Zoran, no faction)
        log.Append(TestEvents.Baseline(eventId: "v1", gameSeconds: 60,
            type: ActivityEventType.Accepted,
            missionType: MissionType.ThirdParty("vganima"),
            sourceSystemId: "sys-zoran"));
        // 1 sourceless event (should not contribute to system/faction counts)
        log.Append(TestEvents.Baseline(eventId: "orphan", gameSeconds: 70,
            type: ActivityEventType.Accepted, missionType: MissionType.Generic));
        return log;
    }

    private static readonly Dictionary<(string From, string To), int> Edges = new()
    {
        { ("sys-zoran", "sys-zoran"),  0 },
        { ("sys-zoran", "sys-helion"), 1 },
        { ("sys-zoran", "sys-vega"),   3 },
    };
    private static int JumpDistance(string from, string to) =>
        Edges.TryGetValue((from, to), out var d) ? d : -1;

    // --- CountByType --------------------------------------------------------

    [Fact]
    public void CountByType_PopulatesBucketsIncludingThirdParty()
    {
        var counts = BuildVariedLog().CountByType();

        Assert.Equal(3, counts[MissionType.Bounty]);
        Assert.Equal(2, counts[MissionType.Patrol]);
        Assert.Equal(1, counts[MissionType.Generic]);
        Assert.Equal(1, counts[MissionType.ThirdParty("vganima")]);
        Assert.False(counts.ContainsKey(MissionType.Story));
    }

    [Fact]
    public void CountByType_WindowedExcludesOutsideEvents()
    {
        var counts = BuildVariedLog().CountByType(sinceGameSeconds: 25, untilGameSeconds: 55);

        Assert.Equal(1, counts[MissionType.Bounty]);   // only b3 @ 30
        Assert.Equal(2, counts[MissionType.Patrol]);   // p1 @ 40, p2 @ 50
        Assert.False(counts.ContainsKey(MissionType.ThirdParty("vganima")));
    }

    [Fact]
    public void CountByType_EmptyLog_ReturnsEmpty()
    {
        Assert.Empty(new ActivityLog().CountByType());
    }

    // --- CountByOutcome -----------------------------------------------------

    [Fact]
    public void CountByOutcome_OnlyTerminals()
    {
        var counts = BuildVariedLog().CountByOutcome();

        Assert.Equal(1, counts[Outcome.Completed]);
        Assert.Equal(1, counts[Outcome.Failed]);
        Assert.Equal(1, counts[Outcome.Abandoned]);
        // Non-terminal Accepted events don't contribute to any bucket.
        Assert.Equal(3, counts.Values.Sum());
    }

    [Fact]
    public void CountByOutcome_EmptyLog_ReturnsEmpty()
    {
        Assert.Empty(new ActivityLog().CountByOutcome());
    }

    // --- CountBySystem ------------------------------------------------------

    [Fact]
    public void CountBySystem_ExcludesSourcelessEvents()
    {
        var counts = BuildVariedLog().CountBySystem();

        Assert.Equal(4, counts["sys-zoran"]);    // b1, b2, b3, v1
        Assert.Equal(2, counts["sys-helion"]);   // p1, p2
        Assert.Equal(2, counts.Count);           // no catch-all for "orphan"
    }

    // --- CountByFaction -----------------------------------------------------

    [Fact]
    public void CountByFaction_ExcludesFactionlessEvents()
    {
        var counts = BuildVariedLog().CountByFaction();

        Assert.Equal(3, counts["BountyGuild"]);   // b1, b2, b3
        Assert.Equal(2, counts["Police"]);        // p1, p2
        Assert.Equal(2, counts.Count);            // v1 (no faction) and orphan excluded
    }

    // --- MostActiveSystemsInRange -------------------------------------------

    [Fact]
    public void MostActiveSystemsInRange_SortsByDescCount()
    {
        var top = BuildVariedLog().MostActiveSystemsInRange(
            pivotSystemId: "sys-zoran",
            jumpDistance: JumpDistance,
            maxJumps: 3,
            topN: 5);

        Assert.Equal(new[] { "sys-zoran", "sys-helion" },
                     top.Select(t => t.SystemId));
        Assert.Equal(new[] { 4, 2 }, top.Select(t => t.Count));
        Assert.Equal(new[] { 0, 1 }, top.Select(t => t.Jumps));
    }

    [Fact]
    public void MostActiveSystemsInRange_TopNCapsResult()
    {
        var top1 = BuildVariedLog().MostActiveSystemsInRange(
            pivotSystemId: "sys-zoran",
            jumpDistance: JumpDistance,
            maxJumps: 3,
            topN: 1);

        Assert.Single(top1);
        Assert.Equal("sys-zoran", top1[0].SystemId);
    }

    [Fact]
    public void MostActiveSystemsInRange_OutOfRangeSystemsExcluded()
    {
        var top = BuildVariedLog().MostActiveSystemsInRange(
            pivotSystemId: "sys-zoran",
            jumpDistance: JumpDistance,
            maxJumps: 0,        // only the pivot itself
            topN: 5);

        Assert.Single(top);
        Assert.Equal("sys-zoran", top[0].SystemId);
    }

    [Fact]
    public void MostActiveSystemsInRange_EmptyLog_ReturnsEmpty()
    {
        var empty = new ActivityLog().MostActiveSystemsInRange(
            "sys-zoran", JumpDistance, maxJumps: 5, topN: 5);

        Assert.Empty(empty);
    }

    [Fact]
    public void MostActiveSystemsInRange_TiesBrokenByOrdinalSystemId()
    {
        var log = new ActivityLog();
        log.Append(TestEvents.Baseline(eventId: "z1", sourceSystemId: "sys-zoran"));
        log.Append(TestEvents.Baseline(eventId: "h1", sourceSystemId: "sys-helion"));

        Dictionary<(string, string), int> localEdges = new()
        {
            { ("pivot", "sys-zoran"),  1 },
            { ("pivot", "sys-helion"), 1 },
        };
        int JumpDistanceLocal(string a, string b) =>
            localEdges.TryGetValue((a, b), out var d) ? d : -1;

        var top = log.MostActiveSystemsInRange(
            "pivot", JumpDistanceLocal, maxJumps: 1, topN: 5);

        // Both count = 1; "sys-helion" < "sys-zoran" ordinally wins the tie.
        Assert.Equal(new[] { "sys-helion", "sys-zoran" }, top.Select(t => t.SystemId));
    }
}
