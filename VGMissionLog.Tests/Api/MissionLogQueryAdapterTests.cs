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

    // --- MissionRecord shape ---------------------------------------------

    [Fact]
    public void GetMission_ReturnsTypedRecord_WithTerminalFields()
    {
        var adapter = new MissionLogQueryAdapter(SampleStore());

        var rec = adapter.GetMission("m1")!;

        Assert.Equal("m1",               rec.MissionInstanceId);
        Assert.Equal("story-m1",         rec.StoryId);
        Assert.Equal("BountyMission",    rec.MissionSubclass);
        Assert.Equal("sys-zoran",        rec.SourceSystemId);
        Assert.Equal("BountyGuild",      rec.SourceFaction);
        Assert.Equal(10.0,               rec.AcceptedAtGameSeconds);
        Assert.Equal(20.0,               rec.TerminalAtGameSeconds);
        Assert.Equal(Outcome.Completed,  rec.Outcome);
        Assert.False(rec.IsActive);
    }

    [Fact]
    public void GetMission_ActiveMission_HasNullTerminalFields()
    {
        var adapter = new MissionLogQueryAdapter(SampleStore());

        var rec = adapter.GetMission("m2")!;

        Assert.True(rec.IsActive);
        Assert.Null(rec.TerminalAtGameSeconds);
        Assert.Null(rec.Outcome);
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
                     active.Select(r => r.MissionInstanceId).ToArray());
    }

    [Fact]
    public void GetAllMissions_ReturnsInsertionOrder()
    {
        var adapter = new MissionLogQueryAdapter(SampleStore());

        var all = adapter.GetAllMissions();

        Assert.Equal(new[] { "m1", "m2", "m3" },
                     all.Select(r => r.MissionInstanceId).ToArray());
    }

    // --- Filter methods ---------------------------------------------------

    [Fact]
    public void GetMissionsInSystem_MatchesBySourceSystemId()
    {
        var adapter = new MissionLogQueryAdapter(SampleStore());

        var zoran = adapter.GetMissionsInSystem("sys-zoran");

        Assert.Equal(new[] { "m1", "m2" },
                     zoran.Select(r => r.MissionInstanceId).ToArray());
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
                     bounties.Select(r => r.MissionInstanceId).ToArray());
    }

    [Fact]
    public void GetMissionsByMissionSubclass_UnknownSubclass_ReturnsEmpty()
    {
        var adapter = new MissionLogQueryAdapter(SampleStore());

        Assert.Empty(adapter.GetMissionsByMissionSubclass("NoSuchMission"));
    }

    [Fact]
    public void GetMissionsByOutcome_FiltersByEnum()
    {
        var adapter = new MissionLogQueryAdapter(SampleStore());

        var completed = adapter.GetMissionsByOutcome(Outcome.Completed);
        Assert.Single(completed);
        Assert.Equal("m1", completed[0].MissionInstanceId);
    }

    [Fact]
    public void GetMissionsByOutcome_NoMatches_ReturnsEmpty()
    {
        var adapter = new MissionLogQueryAdapter(SampleStore());

        Assert.Empty(adapter.GetMissionsByOutcome(Outcome.Failed));
        Assert.Empty(adapter.GetMissionsByOutcome(Outcome.Abandoned));
    }

    [Fact]
    public void GetMissionsForStoryId_ReturnsMatchingRecords()
    {
        var adapter = new MissionLogQueryAdapter(SampleStore());

        var found = adapter.GetMissionsForStoryId("story-m1");

        Assert.Single(found);
        Assert.Equal("m1", found[0].MissionInstanceId);
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
        Assert.Equal(new[] { "k1" }, kills.Select(r => r.MissionInstanceId));
        Assert.Empty(adapter.GetMissionsWithObjective("NoSuchObjective"));
    }

    [Fact]
    public void GetRecentMissions_MostRecentAcceptFirst()
    {
        var adapter = new MissionLogQueryAdapter(SampleStore());

        var recent = adapter.GetRecentMissions(2);

        Assert.Equal(new[] { "m3", "m2" },
                     recent.Select(r => r.MissionInstanceId).ToArray());
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

        Assert.Equal(1, counts[Outcome.Completed]);
        Assert.False(counts.ContainsKey(Outcome.Failed));
        Assert.False(counts.ContainsKey(Outcome.Abandoned));
    }

    [Fact]
    public void MostActiveSystemsInRange_ReturnsTypedSystemActivity()
    {
        var adapter = new MissionLogQueryAdapter(SampleStore());
        var edges = new Dictionary<(string, string), int>
        {
            { ("sys-zoran", "sys-zoran"),  0 },
            { ("sys-zoran", "sys-helion"), 1 },
        };
        int JumpDistance(string a, string b) =>
            edges.TryGetValue((a, b), out var d) ? d : -1;

        var top = adapter.MostActiveSystemsInRange(
            "sys-zoran", JumpDistance, maxJumps: 1, topN: 5);

        Assert.Equal(2, top.Count);
        Assert.Equal("sys-zoran", top[0].SystemId);
        Assert.Equal(2,           top[0].Count);
        Assert.Equal(0,           top[0].Jumps);
        Assert.Equal("sys-helion", top[1].SystemId);
        Assert.Equal(1,            top[1].Count);
        Assert.Equal(1,            top[1].Jumps);
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
