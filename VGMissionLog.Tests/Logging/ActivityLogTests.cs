using System.Linq;
using VGMissionLog.Logging;
using VGMissionLog.Tests.Support;
using Xunit;

namespace VGMissionLog.Tests.Logging;

public class ActivityLogTests
{
    [Fact]
    public void AppendThenRetrieve_PreservesInsertionOrder()
    {
        var log = new ActivityLog();
        var a = TestEvents.Baseline(eventId: "a", gameSeconds: 1.0);
        var b = TestEvents.Baseline(eventId: "b", gameSeconds: 2.0);
        var c = TestEvents.Baseline(eventId: "c", gameSeconds: 3.0);

        log.Append(a);
        log.Append(b);
        log.Append(c);

        Assert.Equal(3, log.TotalEventCount);
        Assert.Equal(new[] { "a", "b", "c" }, log.AllEvents.Select(e => e.EventId));
    }

    [Fact]
    public void Append_AtCap_EvictsOldestFifo()
    {
        var log = new ActivityLog(maxEvents: 3);

        log.Append(TestEvents.Baseline(eventId: "a"));
        log.Append(TestEvents.Baseline(eventId: "b"));
        log.Append(TestEvents.Baseline(eventId: "c"));
        log.Append(TestEvents.Baseline(eventId: "d")); // evicts "a"

        Assert.Equal(3, log.TotalEventCount);
        Assert.Equal(new[] { "b", "c", "d" }, log.AllEvents.Select(e => e.EventId));
    }

    [Fact]
    public void Append_AtCap_FiresFirstEvictionCallbackOnlyOnce()
    {
        var callbackInvocations = 0;
        var capSeen = 0;
        var log = new ActivityLog(
            maxEvents: 2,
            onFirstEviction: cap => { callbackInvocations++; capSeen = cap; });

        log.Append(TestEvents.Baseline(eventId: "a"));
        log.Append(TestEvents.Baseline(eventId: "b"));
        log.Append(TestEvents.Baseline(eventId: "c")); // evicts "a" — first eviction
        log.Append(TestEvents.Baseline(eventId: "d")); // evicts "b" — second eviction; no callback
        log.Append(TestEvents.Baseline(eventId: "e")); // evicts "c" — third eviction; no callback

        Assert.Equal(1, callbackInvocations);
        Assert.Equal(2, capSeen);
    }

    [Fact]
    public void LoadFrom_ClearsExistingThenRebuilds()
    {
        var log = new ActivityLog();
        log.Append(TestEvents.Baseline(eventId: "old-1", storyId: "old-story"));
        log.Append(TestEvents.Baseline(eventId: "old-2", storyId: "old-story"));

        var fresh = new[]
        {
            TestEvents.Baseline(eventId: "new-1", storyId: "new-story"),
            TestEvents.Baseline(eventId: "new-2", storyId: "new-story"),
            TestEvents.Baseline(eventId: "new-3", storyId: "other-story"),
        };
        log.LoadFrom(fresh);

        Assert.Equal(3, log.TotalEventCount);
        Assert.Equal(new[] { "new-1", "new-2", "new-3" }, log.AllEvents.Select(e => e.EventId));
        // Old-story index should be gone; new-story / other-story should be populated.
        Assert.Empty(log.IndexedByStoryId("old-story"));
        Assert.Equal(2, log.IndexedByStoryId("new-story").Count);
        Assert.Single(log.IndexedByStoryId("other-story"));
    }

    [Fact]
    public void LoadFrom_ExceedingCap_TrimsFromFront()
    {
        var log = new ActivityLog(maxEvents: 2);
        var five = new[]
        {
            TestEvents.Baseline(eventId: "1"),
            TestEvents.Baseline(eventId: "2"),
            TestEvents.Baseline(eventId: "3"),
            TestEvents.Baseline(eventId: "4"),
            TestEvents.Baseline(eventId: "5"),
        };
        log.LoadFrom(five);

        Assert.Equal(2, log.TotalEventCount);
        Assert.Equal(new[] { "4", "5" }, log.AllEvents.Select(e => e.EventId));
    }

    [Fact]
    public void IndexedBySourceSystem_ReturnsOnlyEventsForThatSystem()
    {
        var log = new ActivityLog();
        log.Append(TestEvents.Baseline(eventId: "a", sourceSystemId: "sys-zoran"));
        log.Append(TestEvents.Baseline(eventId: "b", sourceSystemId: "sys-zoran"));
        log.Append(TestEvents.Baseline(eventId: "c", sourceSystemId: "sys-helion"));
        log.Append(TestEvents.Baseline(eventId: "d", sourceSystemId: null));

        var zoran = log.IndexedBySourceSystem("sys-zoran");

        Assert.Equal(new[] { "a", "b" }, zoran.Select(e => e.EventId));
        Assert.Empty(log.IndexedBySourceSystem("unknown-system"));
    }

    [Fact]
    public void IndexedBySourceFaction_ReturnsOnlyEventsForThatFaction()
    {
        var log = new ActivityLog();
        log.Append(TestEvents.Baseline(eventId: "a", sourceFaction: "BountyGuild"));
        log.Append(TestEvents.Baseline(eventId: "b", sourceFaction: "TradingGuild"));
        log.Append(TestEvents.Baseline(eventId: "c", sourceFaction: "BountyGuild"));

        var bg = log.IndexedBySourceFaction("BountyGuild");

        Assert.Equal(new[] { "a", "c" }, bg.Select(e => e.EventId));
    }

    [Fact]
    public void IndexedByStoryId_ReturnsFullPerMissionTimeline()
    {
        var log = new ActivityLog();
        log.Append(TestEvents.Baseline(eventId: "acc", storyId: "m1", type: ActivityEventType.Accepted));
        log.Append(TestEvents.Baseline(eventId: "acc2", storyId: "m2", type: ActivityEventType.Accepted));
        log.Append(TestEvents.Baseline(eventId: "done", storyId: "m1", type: ActivityEventType.Completed));

        var m1 = log.IndexedByStoryId("m1");

        Assert.Equal(new[] { "acc", "done" }, m1.Select(e => e.EventId));
        Assert.Equal(new[] { ActivityEventType.Accepted, ActivityEventType.Completed },
                     m1.Select(e => e.Type));
    }

    [Fact]
    public void Eviction_RemovesEventFromAllIndexes()
    {
        var log = new ActivityLog(maxEvents: 2);
        log.Append(TestEvents.Baseline(eventId: "a", storyId: "m1",
            sourceSystemId: "sys-zoran", sourceFaction: "BountyGuild"));
        log.Append(TestEvents.Baseline(eventId: "b", storyId: "m1",
            sourceSystemId: "sys-zoran", sourceFaction: "BountyGuild"));
        log.Append(TestEvents.Baseline(eventId: "c", storyId: "m2",
            sourceSystemId: "sys-helion", sourceFaction: "TradingGuild"));
        // "a" evicted.

        Assert.Equal(new[] { "b" }, log.IndexedByStoryId("m1").Select(e => e.EventId));
        Assert.Equal(new[] { "b" }, log.IndexedBySourceSystem("sys-zoran").Select(e => e.EventId));
        Assert.Equal(new[] { "b" }, log.IndexedBySourceFaction("BountyGuild").Select(e => e.EventId));
    }
}
