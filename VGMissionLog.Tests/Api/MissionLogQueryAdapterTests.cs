using System.Collections.Generic;
using System.Linq;
using VGMissionLog.Api;
using VGMissionLog.Logging;
using VGMissionLog.Persistence;
using VGMissionLog.Tests.Support;
using Xunit;

namespace VGMissionLog.Tests.Api;

[Collection("MissionLogApi.Current")]
public class MissionLogQueryAdapterTests
{
    private static (ActivityLog log, MissionLogQueryAdapter adapter) Build()
    {
        var log = new ActivityLog();
        var adapter = new MissionLogQueryAdapter(log);
        return (log, adapter);
    }

    private static ActivityLog SampleLog()
    {
        var log = new ActivityLog();
        log.Append(TestEvents.Baseline(eventId: "b1", storyId: "m1", gameSeconds: 10,
            type: ActivityEventType.Accepted,  missionSubclass: "BountyMission",
            sourceSystemId: "sys-zoran", sourceFaction: "BountyGuild"));
        log.Append(TestEvents.Baseline(eventId: "b2", storyId: "m1", gameSeconds: 20,
            type: ActivityEventType.Completed, missionSubclass: "BountyMission",
            sourceSystemId: "sys-zoran", sourceFaction: "BountyGuild")
            with { Outcome = Outcome.Completed, RewardsCredits = 1500 });
        log.Append(TestEvents.Baseline(eventId: "v1", storyId: "vganima_llm_x", gameSeconds: 30,
            type: ActivityEventType.Accepted,
            missionSubclass: "Mission",
            sourceSystemId: "sys-helion"));
        return log;
    }

    [Fact]
    public void SchemaVersion_MatchesLogSchema()
    {
        var (_, adapter) = Build();
        Assert.Equal(LogSchema.CurrentVersion, adapter.SchemaVersion);
    }

    [Fact]
    public void PropertyAccessors_EmptyLog_ReturnsDefaults()
    {
        var (_, adapter) = Build();
        Assert.Equal(0, adapter.TotalEventCount);
        Assert.Null(adapter.OldestEventGameSeconds);
        Assert.Null(adapter.NewestEventGameSeconds);
    }

    // --- Event dictionary shape ------------------------------------------

    [Fact]
    public void EventDict_UsesCamelCaseKeys_And_EnumToStrings()
    {
        var adapter = new MissionLogQueryAdapter(SampleLog());

        var results = adapter.GetEventsInSystem("sys-zoran");
        Assert.Equal(2, results.Count);

        var b1 = results.First(r => (string)r["eventId"]! == "b1");
        Assert.Equal("Accepted",           b1["type"]);
        Assert.Equal("BountyMission",      b1["missionSubclass"]);
        Assert.Equal("sys-zoran",          b1["sourceSystemId"]);
        Assert.Equal("BountyGuild",        b1["sourceFaction"]);
        Assert.Equal(10.0,                 b1["gameSeconds"]);
    }

    [Fact]
    public void EventDict_OmitsNullFields_ToKeepConsumerContractSmall()
    {
        var adapter = new MissionLogQueryAdapter(SampleLog());

        var acceptOnly = adapter.GetEventsInSystem("sys-helion").First();

        Assert.False(acceptOnly.ContainsKey("outcome"));
        Assert.False(acceptOnly.ContainsKey("rewardsCredits"));
        Assert.False(acceptOnly.ContainsKey("sourceFaction"));
    }

    [Fact]
    public void EventDict_CompletedCarriesOutcomeAndRewards()
    {
        var adapter = new MissionLogQueryAdapter(SampleLog());

        var completed = adapter.GetEventsInSystem("sys-zoran")
            .First(r => (string)r["eventId"]! == "b2");

        Assert.Equal("Completed", completed["outcome"]);
        Assert.Equal(1500L,       completed["rewardsCredits"]);
    }

    // --- Filter methods ---------------------------------------------------

    [Fact]
    public void GetEventsByMissionSubclass_ExactStringMatch()
    {
        var adapter = new MissionLogQueryAdapter(SampleLog());

        var bounties = adapter.GetEventsByMissionSubclass("BountyMission");

        Assert.Equal(new[] { "b1", "b2" },
                     bounties.Select(r => (string)r["eventId"]!).ToArray());
    }

    [Fact]
    public void GetEventsByMissionSubclass_UnknownSubclass_ReturnsEmpty()
    {
        var adapter = new MissionLogQueryAdapter(SampleLog());

        Assert.Empty(adapter.GetEventsByMissionSubclass("NoSuchMission"));
    }

    [Fact]
    public void GetEventsByOutcome_StringParses_ToEnum()
    {
        var adapter = new MissionLogQueryAdapter(SampleLog());

        var completed = adapter.GetEventsByOutcome("Completed");
        Assert.Single(completed);
        Assert.Equal("b2", completed[0]["eventId"]);
    }

    [Fact]
    public void GetEventsByOutcome_InvalidString_ReturnsEmpty_NotThrow()
    {
        var adapter = new MissionLogQueryAdapter(SampleLog());

        Assert.Empty(adapter.GetEventsByOutcome("Nonsense"));
    }

    [Fact]
    public void GetEventsForStoryId_ReturnsPerMissionTimeline()
    {
        var adapter = new MissionLogQueryAdapter(SampleLog());

        var timeline = adapter.GetEventsForStoryId("m1");

        Assert.Equal(new[] { "b1", "b2" },
                     timeline.Select(r => (string)r["eventId"]!).ToArray());
    }

    [Fact]
    public void GetRecentEvents_MostRecentFirst()
    {
        var adapter = new MissionLogQueryAdapter(SampleLog());

        var recent = adapter.GetRecentEvents(2);

        Assert.Equal(new[] { "v1", "b2" },
                     recent.Select(r => (string)r["eventId"]!).ToArray());
    }

    // --- Proximity + aggregates through the adapter ----------------------

    [Fact]
    public void GetEventsWithinJumps_PassesGraphDelegateThrough()
    {
        var adapter = new MissionLogQueryAdapter(SampleLog());
        var edges = new Dictionary<(string, string), int>
        {
            { ("sys-zoran", "sys-zoran"),  0 },
            { ("sys-zoran", "sys-helion"), 1 },
        };
        int JumpDistance(string a, string b) =>
            edges.TryGetValue((a, b), out var d) ? d : -1;

        var near = adapter.GetEventsWithinJumps("sys-zoran", maxJumps: 1, JumpDistance);

        Assert.Equal(3, near.Count);  // both zoran events + helion event
    }

    [Fact]
    public void CountByMissionSubclass_UsesRawTypeNameKeys()
    {
        var adapter = new MissionLogQueryAdapter(SampleLog());

        var counts = adapter.CountByMissionSubclass();

        Assert.Equal(2, counts["BountyMission"]);
        Assert.Equal(1, counts["Mission"]);
    }

    [Fact]
    public void CountByOutcome_UsesStringKeys_ForOutcome()
    {
        var adapter = new MissionLogQueryAdapter(SampleLog());

        var counts = adapter.CountByOutcome();

        Assert.Equal(1, counts["Completed"]);
        Assert.False(counts.ContainsKey("Failed"));
    }

    [Fact]
    public void MissionLogApi_Current_StartsNull()
    {
        // Plugin.Awake has not run in the test harness; Current must be
        // null so consumers that reflection-probe don't crash on a stale
        // handle. ML-T5b wires this.
        Assert.Null(MissionLogApi.Current);
    }
}
