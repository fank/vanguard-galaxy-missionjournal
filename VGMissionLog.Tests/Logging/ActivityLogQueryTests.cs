using System.Linq;
using VGMissionLog.Logging;
using VGMissionLog.Tests.Support;
using Xunit;

namespace VGMissionLog.Tests.Logging;

public class ActivityLogQueryTests
{
    private static ActivityLog BuildSample()
    {
        // Timeline: t=10 accept bounty in Zoran (BountyGuild),
        //           t=20 accept patrol in Helion (Police),
        //           t=30 complete bounty (m-bounty, Zoran, BountyGuild),
        //           t=40 fail patrol (m-patrol, Helion, Police),
        //           t=50 accept a vganima broker mission (Zoran, no faction).
        var log = new ActivityLog();
        log.Append(TestEvents.Baseline(eventId: "accept-b",  storyId: "m-bounty", gameSeconds: 10,
            type: ActivityEventType.Accepted,
            missionType: MissionType.Bounty,
            sourceSystemId: "sys-zoran",  sourceFaction: "BountyGuild"));
        log.Append(TestEvents.Baseline(eventId: "accept-p",  storyId: "m-patrol", gameSeconds: 20,
            type: ActivityEventType.Accepted,
            missionType: MissionType.Patrol,
            sourceSystemId: "sys-helion", sourceFaction: "Police"));
        log.Append(TestEvents.Baseline(eventId: "complete-b", storyId: "m-bounty", gameSeconds: 30,
            type: ActivityEventType.Completed,
            missionType: MissionType.Bounty,
            sourceSystemId: "sys-zoran",  sourceFaction: "BountyGuild") with { Outcome = Outcome.Completed });
        log.Append(TestEvents.Baseline(eventId: "fail-p",     storyId: "m-patrol", gameSeconds: 40,
            type: ActivityEventType.Failed,
            missionType: MissionType.Patrol,
            sourceSystemId: "sys-helion", sourceFaction: "Police") with { Outcome = Outcome.Failed });
        log.Append(TestEvents.Baseline(eventId: "accept-v",   storyId: "vganima_llm_abc", gameSeconds: 50,
            type: ActivityEventType.Accepted,
            missionType: MissionType.ThirdParty("vganima"),
            sourceSystemId: "sys-zoran"));
        return log;
    }

    [Fact]
    public void GetEventsInSystem_ReturnsOnlyMatchingSource()
    {
        var log = BuildSample();

        var zoran = log.GetEventsInSystem("sys-zoran");
        Assert.Equal(new[] { "accept-b", "complete-b", "accept-v" },
                     zoran.Select(e => e.EventId));
    }

    [Fact]
    public void GetEventsInSystem_RespectsTimeWindow()
    {
        var log = BuildSample();

        // Only t=30 (complete-b) falls in the [25, 45] window among sys-zoran events.
        var windowed = log.GetEventsInSystem("sys-zoran", sinceGameSeconds: 25, untilGameSeconds: 45);
        Assert.Equal(new[] { "complete-b" }, windowed.Select(e => e.EventId));
    }

    [Fact]
    public void GetEventsInSystem_UnknownSystem_ReturnsEmpty()
    {
        Assert.Empty(BuildSample().GetEventsInSystem("sys-unknown"));
    }

    [Fact]
    public void GetEventsByFaction_FiltersByFaction()
    {
        var log = BuildSample();

        var bg = log.GetEventsByFaction("BountyGuild");
        Assert.Equal(new[] { "accept-b", "complete-b" }, bg.Select(e => e.EventId));
    }

    [Fact]
    public void GetEventsByFaction_RespectsTimeWindow()
    {
        var log = BuildSample();

        // [0, 15] catches only accept-b (t=10), not complete-b (t=30).
        var early = log.GetEventsByFaction("BountyGuild", sinceGameSeconds: 0, untilGameSeconds: 15);
        Assert.Equal(new[] { "accept-b" }, early.Select(e => e.EventId));
    }

    [Fact]
    public void GetEventsByMissionType_ExactMatchOnBounty()
    {
        var log = BuildSample();

        var bounty = log.GetEventsByMissionType(MissionType.Bounty);
        Assert.Equal(new[] { "accept-b", "complete-b" }, bounty.Select(e => e.EventId));
    }

    [Fact]
    public void GetEventsByMissionType_ThirdPartyPrefixExactMatch()
    {
        var log = BuildSample();

        var vganima = log.GetEventsByMissionType(MissionType.ThirdParty("vganima"));
        Assert.Equal(new[] { "accept-v" }, vganima.Select(e => e.EventId));

        // A different prefix should not match.
        var other = log.GetEventsByMissionType(MissionType.ThirdParty("someOtherMod"));
        Assert.Empty(other);
    }

    [Fact]
    public void GetEventsByOutcome_ReturnsOnlyTerminalsWithThatOutcome()
    {
        var log = BuildSample();

        Assert.Equal(new[] { "complete-b" },
                     log.GetEventsByOutcome(Outcome.Completed).Select(e => e.EventId));
        Assert.Equal(new[] { "fail-p" },
                     log.GetEventsByOutcome(Outcome.Failed).Select(e => e.EventId));
        Assert.Empty(log.GetEventsByOutcome(Outcome.Abandoned));
    }

    [Fact]
    public void GetEventsForStoryId_ReturnsOrderedTimeline()
    {
        var log = BuildSample();

        var bountyTimeline = log.GetEventsForStoryId("m-bounty");
        Assert.Equal(new[] { "accept-b", "complete-b" }, bountyTimeline.Select(e => e.EventId));
        Assert.Equal(new[] { ActivityEventType.Accepted, ActivityEventType.Completed },
                     bountyTimeline.Select(e => e.Type));
    }

    [Fact]
    public void GetEventsForStoryId_UnknownStory_ReturnsEmpty()
    {
        Assert.Empty(BuildSample().GetEventsForStoryId("never-existed"));
    }

    [Fact]
    public void GetRecentEvents_ReturnsMostRecentFirst()
    {
        var log = BuildSample();

        var recent3 = log.GetRecentEvents(3);
        Assert.Equal(new[] { "accept-v", "fail-p", "complete-b" },
                     recent3.Select(e => e.EventId));
    }

    [Fact]
    public void GetRecentEvents_AppliesFilter()
    {
        var log = BuildSample();

        var recentAcceptances = log.GetRecentEvents(
            count: 10,
            filter: e => e.Type == ActivityEventType.Accepted);
        Assert.Equal(new[] { "accept-v", "accept-p", "accept-b" },
                     recentAcceptances.Select(e => e.EventId));
    }

    [Fact]
    public void GetRecentEvents_CountZeroOrNegative_ReturnsEmpty()
    {
        var log = BuildSample();

        Assert.Empty(log.GetRecentEvents(0));
        Assert.Empty(log.GetRecentEvents(-3));
    }

    [Fact]
    public void OldestAndNewestGameSeconds_ReflectInsertionOrder()
    {
        var log = new ActivityLog();
        Assert.Null(log.OldestEventGameSeconds);
        Assert.Null(log.NewestEventGameSeconds);

        log.Append(TestEvents.Baseline(eventId: "a", gameSeconds: 100));
        log.Append(TestEvents.Baseline(eventId: "b", gameSeconds: 42));   // earlier game-time, later insertion
        log.Append(TestEvents.Baseline(eventId: "c", gameSeconds: 200));

        // Spec uses insertion order for "oldest" / "newest" (not
        // min/max gameSeconds) — events are always appended in wall-clock
        // order so the first appended has the earliest real-time capture.
        Assert.Equal(100, log.OldestEventGameSeconds);
        Assert.Equal(200, log.NewestEventGameSeconds);
    }
}
