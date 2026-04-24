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
        //           t=50 accept a custom-storyId parametric mission (Zoran, no faction).
        var log = new ActivityLog();
        log.Append(TestEvents.Baseline(eventId: "accept-b",  storyId: "m-bounty", gameSeconds: 10,
            type: ActivityEventType.Accepted,
            missionSubclass: "BountyMission",
            sourceSystemId: "sys-zoran",  sourceFaction: "BountyGuild"));
        log.Append(TestEvents.Baseline(eventId: "accept-p",  storyId: "m-patrol", gameSeconds: 20,
            type: ActivityEventType.Accepted,
            missionSubclass: "PatrolMission",
            sourceSystemId: "sys-helion", sourceFaction: "Police"));
        log.Append(TestEvents.Baseline(eventId: "complete-b", storyId: "m-bounty", gameSeconds: 30,
            type: ActivityEventType.Completed,
            missionSubclass: "BountyMission",
            sourceSystemId: "sys-zoran",  sourceFaction: "BountyGuild") with { Outcome = Outcome.Completed });
        log.Append(TestEvents.Baseline(eventId: "fail-p",     storyId: "m-patrol", gameSeconds: 40,
            type: ActivityEventType.Failed,
            missionSubclass: "PatrolMission",
            sourceSystemId: "sys-helion", sourceFaction: "Police") with { Outcome = Outcome.Failed });
        log.Append(TestEvents.Baseline(eventId: "accept-v",   storyId: "story-custom-abc", gameSeconds: 50,
            type: ActivityEventType.Accepted,
            missionSubclass: "Mission",
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
    public void GetEventsByMissionSubclass_ExactMatchOnBountyMission()
    {
        var log = BuildSample();

        var bounty = log.GetEventsByMissionSubclass("BountyMission");
        Assert.Equal(new[] { "accept-b", "complete-b" }, bounty.Select(e => e.EventId));
    }

    [Fact]
    public void GetEventsByMissionSubclass_UnknownSubclass_ReturnsEmpty()
    {
        Assert.Empty(BuildSample().GetEventsByMissionSubclass("NoSuchMission"));
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
    public void GetEventsWithObjective_MatchesAnyStepAndObjective()
    {
        var log = new ActivityLog();

        var killStep = new MissionStepSnapshot(
            Description: null, IsComplete: false, RequireAllObjectives: true, Hidden: false,
            Objectives: new[]
            {
                new MissionObjectiveSnapshot("KillEnemies",  IsComplete: false, StatusText: null, Fields: null),
                new MissionObjectiveSnapshot("TravelToPOI",  IsComplete: false, StatusText: null, Fields: null),
            });
        var collectStep = new MissionStepSnapshot(
            Description: null, IsComplete: false, RequireAllObjectives: true, Hidden: false,
            Objectives: new[]
            {
                new MissionObjectiveSnapshot("CollectItemTypes", IsComplete: false, StatusText: null, Fields: null),
            });

        log.Append(TestEvents.Baseline(eventId: "kill-only", gameSeconds: 10)
            with { Steps = new[] { killStep } });
        log.Append(TestEvents.Baseline(eventId: "collect-only", gameSeconds: 20)
            with { Steps = new[] { collectStep } });
        log.Append(TestEvents.Baseline(eventId: "multi-step", gameSeconds: 30)
            with { Steps = new[] { killStep, collectStep } });
        log.Append(TestEvents.Baseline(eventId: "no-steps", gameSeconds: 40));    // Steps = null

        Assert.Equal(new[] { "kill-only", "multi-step" },
                     log.GetEventsWithObjective("KillEnemies").Select(e => e.EventId));
        Assert.Equal(new[] { "collect-only", "multi-step" },
                     log.GetEventsWithObjective("CollectItemTypes").Select(e => e.EventId));
        Assert.Empty(log.GetEventsWithObjective("kill_enemies"));        // case-sensitive
        Assert.Empty(log.GetEventsWithObjective("NoSuchObjective"));
        Assert.Empty(log.GetEventsWithObjective(""));
    }

    [Fact]
    public void GetEventsWithObjective_RespectsTimeWindow()
    {
        var log = new ActivityLog();
        var step = new MissionStepSnapshot(
            Description: null, IsComplete: false, RequireAllObjectives: true, Hidden: false,
            Objectives: new[]
            {
                new MissionObjectiveSnapshot("KillEnemies", IsComplete: false, StatusText: null, Fields: null),
            });
        log.Append(TestEvents.Baseline(eventId: "early", gameSeconds: 10) with { Steps = new[] { step } });
        log.Append(TestEvents.Baseline(eventId: "late",  gameSeconds: 50) with { Steps = new[] { step } });

        Assert.Equal(new[] { "late" },
                     log.GetEventsWithObjective("KillEnemies", sinceGameSeconds: 30)
                        .Select(e => e.EventId));
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
