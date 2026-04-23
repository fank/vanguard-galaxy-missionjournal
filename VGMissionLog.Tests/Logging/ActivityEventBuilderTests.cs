using System.Linq;
using Source.Player;
using VGMissionLog.Logging;
using VGMissionLog.Tests.Support;
using Xunit;

namespace VGMissionLog.Tests.Logging;

public class ActivityEventBuilderTests
{
    private readonly FakeClock _clock = new() { GameSeconds = 1234.5 };
    private ActivityEventBuilder NewBuilder() =>
        // Null game-player provider since we can't construct a real one in tests.
        new(_clock, () => (GamePlayer?)null);

    [Fact]
    public void Build_PopulatesCoreFields()
    {
        var mission = TestMission.Bounty();
        mission.name = "Pirate Hunt";

        var evt = NewBuilder().Build(mission, ActivityEventType.Accepted);

        Assert.False(string.IsNullOrEmpty(evt.EventId));
        Assert.Equal(ActivityEventType.Accepted, evt.Type);
        Assert.Equal(1234.5,                      evt.GameSeconds);
        Assert.Equal("Pirate Hunt",               evt.MissionName);
        Assert.Equal("BountyMission",             evt.MissionSubclass);
    }

    [Fact]
    public void Build_PropagatesClockTimestamps()
    {
        _clock.GameSeconds = 999.0;
        _clock.UtcNow      = new System.DateTime(2026, 4, 23, 20, 30, 0, System.DateTimeKind.Utc);

        var evt = NewBuilder().Build(TestMission.Generic(), ActivityEventType.Accepted);

        Assert.Equal(999.0, evt.GameSeconds);
        // RealUtc is ISO-8601 with DateTime.ToString("O") — starts with the date.
        Assert.StartsWith("2026-04-23T20:30:00", evt.RealUtc);
    }

    [Fact]
    public void Build_EachInvocation_GeneratesUniqueEventId()
    {
        var b = NewBuilder();
        var a1 = b.Build(TestMission.Generic(), ActivityEventType.Accepted);
        var a2 = b.Build(TestMission.Generic(), ActivityEventType.Accepted);

        Assert.NotEqual(a1.EventId, a2.EventId);
    }

    // --- Story id passthrough --------------------------------------------

    [Fact]
    public void Build_StoryIdPresent_PassesThrough()
    {
        var mission = TestMission.Generic("story-xyz");

        var evt = NewBuilder().Build(mission, ActivityEventType.Accepted);

        Assert.Equal("story-xyz", evt.StoryId);
    }

    [Fact]
    public void Build_StoryIdAbsent_YieldsEmptyString()
    {
        // The game does not hand us a storyId for parametric missions; we
        // do not fabricate one. Empty string is the honest representation.
        var evt = NewBuilder().Build(TestMission.Generic(), ActivityEventType.Accepted);

        Assert.Equal(string.Empty, evt.StoryId);
    }

    // --- Outcome derivation ----------------------------------------------

    [Theory]
    [InlineData(ActivityEventType.Completed, Outcome.Completed)]
    [InlineData(ActivityEventType.Failed,    Outcome.Failed)]
    [InlineData(ActivityEventType.Abandoned, Outcome.Abandoned)]
    public void Build_TerminalTypes_DeriveMatchingOutcome(ActivityEventType type, Outcome expected)
    {
        var evt = NewBuilder().Build(TestMission.Generic(), type);

        Assert.Equal(expected, evt.Outcome);
    }

    [Theory]
    [InlineData(ActivityEventType.Offered)]
    [InlineData(ActivityEventType.Accepted)]
    [InlineData(ActivityEventType.ObjectiveProgressed)]
    public void Build_NonTerminalTypes_LeaveOutcomeNull(ActivityEventType type)
    {
        var evt = NewBuilder().Build(TestMission.Generic(), type);

        Assert.Null(evt.Outcome);
    }

    // --- Reward extraction ------------------------------------------------

    [Fact]
    public void Build_Completed_ExtractsCreditsAndExperienceRewards()
    {
        var mission = TestMission.GenericWithRewards(
            TestMission.Credits(1500),
            TestMission.Experience(200));

        var evt = NewBuilder().Build(mission, ActivityEventType.Completed);

        Assert.Equal(1500L, evt.RewardsCredits);
        Assert.Equal(200L,  evt.RewardsExperience);
        Assert.Null(evt.RewardsReputation);
    }

    [Fact]
    public void Build_Completed_CapturesReputationRewardsWithUnknownFactionFallback()
    {
        // We can't construct a real Faction in xUnit (its static ctor
        // self-references vanilla's Faction.Gold/Red/Blue whose ctors die
        // outside Unity). The builder's `ReadFactionId(null) ?? "unknown"`
        // path is what we exercise here. In production, rep.faction is
        // always populated by Faction.Get and the real identifier is
        // captured — covered by ML-T7b in-game manual E2E.
        var mission = TestMission.GenericWithRewards(
            TestMission.Reputation(5),
            TestMission.Reputation(-2));

        var evt = NewBuilder().Build(mission, ActivityEventType.Completed);

        Assert.NotNull(evt.RewardsReputation);
        var rep = evt.RewardsReputation!.ToList();
        Assert.Equal(2, rep.Count);
        Assert.Contains(rep, r => r.Faction == "unknown" && r.Amount == 5);
        Assert.Contains(rep, r => r.Faction == "unknown" && r.Amount == -2);
    }

    [Fact]
    public void Build_Completed_SumsMultipleCreditsRewards()
    {
        // A mission with two Credits entries (e.g., base + bonus) should sum.
        var mission = TestMission.GenericWithRewards(
            TestMission.Credits(1000),
            TestMission.Credits(500));

        var evt = NewBuilder().Build(mission, ActivityEventType.Completed);

        Assert.Equal(1500L, evt.RewardsCredits);
    }

    [Fact]
    public void Build_NonCompleted_LeavesRewardsNull_EvenWithPopulatedMission()
    {
        // An Accepted event doesn't care about rewards (they haven't been
        // paid out yet); the builder skips extraction entirely.
        var mission = TestMission.GenericWithRewards(
            TestMission.Credits(1000));

        var evt = NewBuilder().Build(mission, ActivityEventType.Accepted);

        Assert.Null(evt.RewardsCredits);
        Assert.Null(evt.RewardsExperience);
        Assert.Null(evt.RewardsReputation);
    }

    [Fact]
    public void Build_Completed_EmptyRewardList_LeavesAllRewardFieldsNull()
    {
        var mission = TestMission.GenericWithRewards(); // zero rewards
        var evt = NewBuilder().Build(mission, ActivityEventType.Completed);

        Assert.Null(evt.RewardsCredits);
        Assert.Null(evt.RewardsExperience);
        Assert.Null(evt.RewardsReputation);
    }

    // --- Null-safety in test runtime --------------------------------------

    [Fact]
    public void Build_NullGamePlayer_PlayerFieldsFallBackToNullOrZero()
    {
        var evt = NewBuilder().Build(TestMission.Generic(), ActivityEventType.Accepted);

        Assert.Equal(0, evt.PlayerLevel);
        Assert.Null(evt.PlayerShipName);
        Assert.Null(evt.PlayerShipLevel);
        Assert.Null(evt.PlayerCurrentSystemId);
    }

    // --- Mission instance id ---------------------------------------------

    [Fact]
    public void Build_SameMissionInstance_YieldsStableInstanceId()
    {
        // The id lets consumers link Accepted → Completed for generator
        // missions where vanilla leaves storyId empty. Two events built
        // from the same Mission reference must share the same id.
        var mission = TestMission.Generic();
        var b = NewBuilder();

        var accepted  = b.Build(mission, ActivityEventType.Accepted);
        var completed = b.Build(mission, ActivityEventType.Completed);

        Assert.False(string.IsNullOrEmpty(accepted.MissionInstanceId));
        Assert.Equal(accepted.MissionInstanceId, completed.MissionInstanceId);
    }

    [Fact]
    public void Build_DifferentMissionInstances_YieldDistinctInstanceIds()
    {
        var b = NewBuilder();

        var a = b.Build(TestMission.Generic(), ActivityEventType.Accepted);
        var c = b.Build(TestMission.Generic(), ActivityEventType.Accepted);

        Assert.NotEqual(a.MissionInstanceId, c.MissionInstanceId);
    }

    // --- Step / objective snapshot ---------------------------------------

    [Fact]
    public void Build_NoSteps_YieldsNullStepsList()
    {
        // A bare Mission from GetUninitializedObject has steps=null.
        // The builder treats null as "unreadable", emitting null so the
        // mapper omits the key.
        var evt = NewBuilder().Build(TestMission.Generic(), ActivityEventType.Accepted);

        Assert.Null(evt.Steps);
    }

    [Fact]
    public void Build_WithStep_CapturesObjectiveType()
    {
        // One step, one KillEnemies objective with requiredAmount=5.
        var kill = TestMission.Kill();
        kill.requiredAmount = 5;
        kill.shipType = "Pirate";
        var mission = TestMission.WithObjectives(kill);

        var evt = NewBuilder().Build(mission, ActivityEventType.Accepted);

        Assert.NotNull(evt.Steps);
        var steps = evt.Steps!.ToList();
        Assert.Single(steps);
        var step = steps[0];
        Assert.Single(step.Objectives);
        var obj = step.Objectives[0];
        Assert.Equal("KillEnemies", obj.Type);
        Assert.False(obj.IsComplete);                       // currentAmount=0, required=5
        Assert.NotNull(obj.Fields);
        Assert.Equal(5,        obj.Fields!["requiredAmount"]);
        Assert.Equal("Pirate", obj.Fields!["shipType"]);
    }

    [Fact]
    public void Build_MultipleSteps_PreservesOrder()
    {
        var s1 = TestMission.BuildStep(TestMission.Travel());
        var s2 = TestMission.BuildStep(TestMission.Kill());
        var mission = TestMission.WithSteps(s1, s2);

        var evt = NewBuilder().Build(mission, ActivityEventType.Accepted);

        Assert.NotNull(evt.Steps);
        var steps = evt.Steps!.ToList();
        Assert.Equal(2, steps.Count);
        Assert.Equal("TravelToPOI", steps[0].Objectives[0].Type);
        Assert.Equal("KillEnemies", steps[1].Objectives[0].Type);
    }

    [Fact]
    public void Build_NullMission_Throws()
    {
        Assert.Throws<System.ArgumentNullException>(() =>
            NewBuilder().Build(null!, ActivityEventType.Accepted));
    }
}
