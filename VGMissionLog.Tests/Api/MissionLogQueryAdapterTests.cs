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
    private static (MissionStore store, MissionLogQueryAdapter adapter) Build()
    {
        var store   = new MissionStore();
        var adapter = new MissionLogQueryAdapter(store);
        return (store, adapter);
    }

    /// <summary>
    /// Three-mission fixture:
    ///  - m1: BountyMission in sys-zoran/BountyGuild, accepted t=10, completed t=20
    ///  - m2: still active, BountyMission in sys-zoran/BountyGuild, accepted t=15
    ///  - m3: Mission in sys-helion, accepted t=30, no faction
    /// </summary>
    private static MissionStore SampleStore()
    {
        var store = new MissionStore();
        store.Upsert(TestRecords.Record(
            instanceId: "m1", storyId: "story-m1", acceptedAt: 10,
            terminal: TimelineState.Completed, terminalAt: 20,
            subclass: "BountyMission",
            sourceSystemId: "sys-zoran", sourceFaction: "BountyGuild"));
        store.Upsert(TestRecords.Record(
            instanceId: "m2", storyId: "story-m2", acceptedAt: 15,
            subclass: "BountyMission",
            sourceSystemId: "sys-zoran", sourceFaction: "BountyGuild"));
        store.Upsert(TestRecords.Record(
            instanceId: "m3", storyId: "story-m3", acceptedAt: 30,
            subclass: "Mission",
            sourceSystemId: "sys-helion"));
        return store;
    }

    [Fact]
    public void SchemaVersion_MatchesLogSchema()
    {
        var (_, adapter) = Build();
        Assert.Equal(LogSchema.CurrentVersion, adapter.SchemaVersion);
    }

    [Fact]
    public void PropertyAccessors_EmptyStore_ReturnsDefaults()
    {
        var (_, adapter) = Build();
        Assert.Equal(0, adapter.TotalMissionCount);
        Assert.Null(adapter.OldestAcceptedGameSeconds);
        Assert.Null(adapter.NewestAcceptedGameSeconds);
    }

    [Fact]
    public void PropertyAccessors_PopulatedStore_ReturnsBounds()
    {
        var adapter = new MissionLogQueryAdapter(SampleStore());
        Assert.Equal(3, adapter.TotalMissionCount);
        Assert.Equal(10.0, adapter.OldestAcceptedGameSeconds);
        Assert.Equal(30.0, adapter.NewestAcceptedGameSeconds);
    }

    // --- Mission dict shape ----------------------------------------------

    [Fact]
    public void MissionDict_UsesCamelCaseKeys_AndStringEnums()
    {
        var adapter = new MissionLogQueryAdapter(SampleStore());

        var dict = adapter.GetMission("m1")!;

        Assert.Equal("m1",                dict["missionInstanceId"]);
        Assert.Equal("story-m1",          dict["storyId"]);
        Assert.Equal("BountyMission",     dict["missionSubclass"]);
        Assert.Equal("sys-zoran",         dict["sourceSystemId"]);
        Assert.Equal("BountyGuild",       dict["sourceFaction"]);
        Assert.Equal(10.0,                dict["acceptedAtGameSeconds"]);
        Assert.Equal(20.0,                dict["terminalAtGameSeconds"]);
        Assert.Equal("Completed",         dict["outcome"]);
        Assert.Equal(false,               dict["isActive"]);
    }

    [Fact]
    public void MissionDict_ActiveMission_HasNullTerminalFields()
    {
        var adapter = new MissionLogQueryAdapter(SampleStore());

        var dict = adapter.GetMission("m2")!;

        Assert.Equal(true, dict["isActive"]);
        Assert.Null(dict["terminalAtGameSeconds"]);
        Assert.Null(dict["outcome"]);
    }

    [Fact]
    public void GetMission_UnknownId_ReturnsNull()
    {
        var adapter = new MissionLogQueryAdapter(SampleStore());
        Assert.Null(adapter.GetMission("no-such-id"));
    }

    [Fact]
    public void GetActiveMissions_ExcludesTerminated()
    {
        var adapter = new MissionLogQueryAdapter(SampleStore());

        var active = adapter.GetActiveMissions();

        Assert.Equal(new[] { "m2", "m3" },
                     active.Select(r => (string)r["missionInstanceId"]!).ToArray());
    }

    [Fact]
    public void GetAllMissions_ReturnsInsertionOrder()
    {
        var adapter = new MissionLogQueryAdapter(SampleStore());

        var all = adapter.GetAllMissions();

        Assert.Equal(new[] { "m1", "m2", "m3" },
                     all.Select(r => (string)r["missionInstanceId"]!).ToArray());
    }

    // --- Filter methods ---------------------------------------------------

    [Fact]
    public void GetMissionsInSystem_MatchesBySourceSystemId()
    {
        var adapter = new MissionLogQueryAdapter(SampleStore());

        var zoran = adapter.GetMissionsInSystem("sys-zoran");

        Assert.Equal(new[] { "m1", "m2" },
                     zoran.Select(r => (string)r["missionInstanceId"]!).ToArray());
    }

    [Fact]
    public void GetMissionsByFaction_MatchesBySourceFaction()
    {
        var adapter = new MissionLogQueryAdapter(SampleStore());

        var guild = adapter.GetMissionsByFaction("BountyGuild");

        Assert.Equal(2, guild.Count);
    }

    [Fact]
    public void GetMissionsByMissionSubclass_ExactStringMatch()
    {
        var adapter = new MissionLogQueryAdapter(SampleStore());

        var bounties = adapter.GetMissionsByMissionSubclass("BountyMission");

        Assert.Equal(new[] { "m1", "m2" },
                     bounties.Select(r => (string)r["missionInstanceId"]!).ToArray());
    }

    [Fact]
    public void GetMissionsByMissionSubclass_UnknownSubclass_ReturnsEmpty()
    {
        var adapter = new MissionLogQueryAdapter(SampleStore());

        Assert.Empty(adapter.GetMissionsByMissionSubclass("NoSuchMission"));
    }

    [Fact]
    public void GetMissionsByOutcome_StringParses_ToEnum()
    {
        var adapter = new MissionLogQueryAdapter(SampleStore());

        var completed = adapter.GetMissionsByOutcome("Completed");
        Assert.Single(completed);
        Assert.Equal("m1", completed[0]["missionInstanceId"]);
    }

    [Fact]
    public void GetMissionsByOutcome_InvalidString_ReturnsEmpty_NotThrow()
    {
        var adapter = new MissionLogQueryAdapter(SampleStore());

        Assert.Empty(adapter.GetMissionsByOutcome("Nonsense"));
    }

    [Fact]
    public void GetMissionsForStoryId_ReturnsMatchingRecords()
    {
        var adapter = new MissionLogQueryAdapter(SampleStore());

        var found = adapter.GetMissionsForStoryId("story-m1");

        Assert.Single(found);
        Assert.Equal("m1", found[0]["missionInstanceId"]);
    }

    [Fact]
    public void GetMissionsWithObjective_FiltersByObjectiveTypeName()
    {
        var store = new MissionStore();
        var step = new MissionStepDefinition(
            Description: null, RequireAllObjectives: true, Hidden: false,
            Objectives: new[]
            {
                new MissionObjectiveDefinition("KillEnemies", Fields: null),
            });
        store.Upsert(TestRecords.Record(
            instanceId: "k1", storyId: "m-k", acceptedAt: 10,
            steps: new[] { step }));
        store.Upsert(TestRecords.Record(
            instanceId: "n1", storyId: "m-n", acceptedAt: 20));
        var adapter = new MissionLogQueryAdapter(store);

        var kills = adapter.GetMissionsWithObjective("KillEnemies");
        Assert.Equal(new[] { "k1" }, kills.Select(r => (string)r["missionInstanceId"]!));
        Assert.Empty(adapter.GetMissionsWithObjective("NoSuchObjective"));
    }

    [Fact]
    public void GetRecentMissions_MostRecentAcceptFirst()
    {
        var adapter = new MissionLogQueryAdapter(SampleStore());

        var recent = adapter.GetRecentMissions(2);

        Assert.Equal(new[] { "m3", "m2" },
                     recent.Select(r => (string)r["missionInstanceId"]!).ToArray());
    }

    // --- Proximity + aggregates ------------------------------------------

    [Fact]
    public void GetMissionsWithinJumps_PassesGraphDelegateThrough()
    {
        var adapter = new MissionLogQueryAdapter(SampleStore());
        var edges = new Dictionary<(string, string), int>
        {
            { ("sys-zoran", "sys-zoran"),  0 },
            { ("sys-zoran", "sys-helion"), 1 },
        };
        int JumpDistance(string a, string b) =>
            edges.TryGetValue((a, b), out var d) ? d : -1;

        var near = adapter.GetMissionsWithinJumps("sys-zoran", maxJumps: 1, JumpDistance);

        Assert.Equal(3, near.Count);  // both zoran missions + helion
    }

    [Fact]
    public void CountByMissionSubclass_UsesRawTypeNameKeys()
    {
        var adapter = new MissionLogQueryAdapter(SampleStore());

        var counts = adapter.CountByMissionSubclass();

        Assert.Equal(2, counts["BountyMission"]);
        Assert.Equal(1, counts["Mission"]);
    }

    [Fact]
    public void CountByOutcome_OnlyCountsTerminatedMissions()
    {
        var adapter = new MissionLogQueryAdapter(SampleStore());

        var counts = adapter.CountByOutcome();

        Assert.Equal(1, counts["Completed"]);
        Assert.False(counts.ContainsKey("Failed"));
        Assert.False(counts.ContainsKey("Abandoned"));
    }

    [Fact]
    public void MissionLogApi_Current_StartsNull()
    {
        // Plugin.Awake has not run in the test harness; Current must be
        // null so consumers that reflection-probe don't crash on a stale
        // handle.
        Assert.Null(MissionLogApi.Current);
    }
}
