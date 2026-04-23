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
        Assert.Equal(MissionType.Bounty,          evt.MissionType);
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

    // --- Classification integration --------------------------------------

    [Fact]
    public void Build_ClassifiesThirdPartyVganima_FromStoryId()
    {
        var mission = TestMission.Generic("vganima_llm_xyz");

        var evt = NewBuilder().Build(mission, ActivityEventType.Accepted);

        Assert.Equal(MissionType.ThirdParty("vganima"), evt.MissionType);
        Assert.Equal("vganima_llm_xyz", evt.StoryId);
    }

    [Fact]
    public void Build_GenericMission_HasEmptyStoryId_NotNull()
    {
        // R1.2 says storyId is `string` (non-nullable) — synthesise "" for
        // missions that don't carry one, rather than leaving a null on the wire.
        var evt = NewBuilder().Build(TestMission.Generic(), ActivityEventType.Accepted);

        Assert.Equal("", evt.StoryId);
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

    [Fact]
    public void Build_NullMission_Throws()
    {
        Assert.Throws<System.ArgumentNullException>(() =>
            NewBuilder().Build(null!, ActivityEventType.Accepted));
    }

    // --- FacilityOrigin integration ---------------------------------------

    [Fact]
    public void Build_BountyMission_GetsBountyBoardFacilityOrigin()
    {
        var evt = NewBuilder().Build(TestMission.Bounty(), ActivityEventType.Accepted);

        Assert.Equal(FacilityOrigin.BountyBoard, evt.FacilityOrigin);
    }
}
