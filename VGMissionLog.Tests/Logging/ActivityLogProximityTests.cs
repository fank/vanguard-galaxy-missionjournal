using System.Collections.Generic;
using System.Linq;
using VGMissionLog.Logging;
using VGMissionLog.Tests.Support;
using Xunit;

namespace VGMissionLog.Tests.Logging;

public class ActivityLogProximityTests
{
    /// <summary>
    /// Toy star-map used by every test in this class. Zoran is the hub;
    /// Helion is 1 jump; Vega is 2 jumps; Tarsis is 3 jumps; Isolation is
    /// explicitly unreachable from Zoran.
    /// </summary>
    private static readonly Dictionary<(string From, string To), int> Edges = new()
    {
        { ("sys-zoran",     "sys-zoran"),     0 },
        { ("sys-zoran",     "sys-helion"),    1 },
        { ("sys-zoran",     "sys-vega"),      2 },
        { ("sys-zoran",     "sys-tarsis"),    3 },
        { ("sys-zoran",     "sys-isolation"), -1 },
    };

    private static int JumpDistance(string from, string to) =>
        Edges.TryGetValue((from, to), out var d) ? d : -1;

    private static ActivityLog BuildSample()
    {
        var log = new ActivityLog();
        log.Append(TestEvents.Baseline(eventId: "hub",      gameSeconds: 10, sourceSystemId: "sys-zoran"));
        log.Append(TestEvents.Baseline(eventId: "near",     gameSeconds: 20, sourceSystemId: "sys-helion"));
        log.Append(TestEvents.Baseline(eventId: "mid",      gameSeconds: 30, sourceSystemId: "sys-vega"));
        log.Append(TestEvents.Baseline(eventId: "far",      gameSeconds: 40, sourceSystemId: "sys-tarsis"));
        log.Append(TestEvents.Baseline(eventId: "lost",     gameSeconds: 50, sourceSystemId: "sys-isolation"));
        log.Append(TestEvents.Baseline(eventId: "nowhere",  gameSeconds: 60, sourceSystemId: null));
        return log;
    }

    [Fact]
    public void WithinJumps_MaxZero_ReturnsOnlyPivotSystem()
    {
        var log = BuildSample();

        var hub = log.GetEventsWithinJumps("sys-zoran", maxJumps: 0, JumpDistance);

        Assert.Equal(new[] { "hub" }, hub.Select(e => e.EventId));
    }

    [Fact]
    public void WithinJumps_MaxOne_IncludesHubAndDirectNeighbor()
    {
        var log = BuildSample();

        var within1 = log.GetEventsWithinJumps("sys-zoran", maxJumps: 1, JumpDistance);

        Assert.Equal(new[] { "hub", "near" }, within1.Select(e => e.EventId));
    }

    [Fact]
    public void WithinJumps_UnreachableSystems_AreExcluded()
    {
        var log = BuildSample();

        var within10 = log.GetEventsWithinJumps("sys-zoran", maxJumps: 10, JumpDistance);

        // "lost" (isolation, unreachable) and "nowhere" (no source system) never appear.
        Assert.Equal(new[] { "hub", "near", "mid", "far" }, within10.Select(e => e.EventId));
        Assert.DoesNotContain(within10, e => e.EventId == "lost");
        Assert.DoesNotContain(within10, e => e.EventId == "nowhere");
    }

    [Fact]
    public void WithinJumps_RespectsTimeWindow()
    {
        var log = BuildSample();

        // [25, 45] catches "mid" (30) and "far" (40); they're 2 and 3 jumps away,
        // so maxJumps=3 includes both.
        var windowed = log.GetEventsWithinJumps(
            pivotSystemId: "sys-zoran",
            maxJumps: 3,
            jumpDistance: JumpDistance,
            sinceGameSeconds: 25,
            untilGameSeconds: 45);

        Assert.Equal(new[] { "mid", "far" }, windowed.Select(e => e.EventId));
    }

    [Fact]
    public void WithinJumps_NegativeMaxJumps_ReturnsEmpty()
    {
        var log = BuildSample();

        Assert.Empty(log.GetEventsWithinJumps("sys-zoran", maxJumps: -1, JumpDistance));
    }

    [Fact]
    public void SortedByJumps_AscendingDistance()
    {
        var log = BuildSample();

        var sorted = log.GetEventsSortedByJumps("sys-zoran", JumpDistance).ToList();

        Assert.Equal(new[] { "hub", "near", "mid", "far" },
                     sorted.Select(p => p.Event.EventId));
        Assert.Equal(new[] { 0, 1, 2, 3 }, sorted.Select(p => p.Jumps));
    }

    [Fact]
    public void SortedByJumps_TiesBrokenByInsertionOrder()
    {
        var log = new ActivityLog();
        log.Append(TestEvents.Baseline(eventId: "helion-a", sourceSystemId: "sys-helion"));
        log.Append(TestEvents.Baseline(eventId: "helion-b", sourceSystemId: "sys-helion"));

        var sorted = log.GetEventsSortedByJumps("sys-zoran", JumpDistance).ToList();

        // Both are 1 jump; insertion order is the tiebreaker.
        Assert.Equal(new[] { "helion-a", "helion-b" }, sorted.Select(p => p.Event.EventId));
    }

    [Fact]
    public void SortedByJumps_AppliesOptionalFilter()
    {
        var log = BuildSample();

        var sorted = log.GetEventsSortedByJumps(
            "sys-zoran",
            JumpDistance,
            filter: e => e.GameSeconds >= 30).ToList();

        Assert.Equal(new[] { "mid", "far" }, sorted.Select(p => p.Event.EventId));
    }

    [Fact]
    public void SortedByJumps_SkipsUnreachableAndSourcelessEvents()
    {
        var log = BuildSample();

        var sorted = log.GetEventsSortedByJumps("sys-zoran", JumpDistance).ToList();

        Assert.DoesNotContain(sorted, p => p.Event.EventId == "lost");
        Assert.DoesNotContain(sorted, p => p.Event.EventId == "nowhere");
    }
}
