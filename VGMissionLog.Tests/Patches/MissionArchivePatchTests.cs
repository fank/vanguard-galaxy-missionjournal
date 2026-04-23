using System.Linq;
using System.Reflection;
using HarmonyLib;
using Source.Player;
using VGMissionLog.Logging;
using VGMissionLog.Patches;
using VGMissionLog.Tests.Support;
using Xunit;

namespace VGMissionLog.Tests.Patches;

public class MissionArchivePatchTests
{
    [Fact]
    public void TargetMethod_ResolvesTo_ArchiveMission()
    {
        var target = typeof(GamePlayer).GetMethod(
            nameof(GamePlayer.ArchiveMission),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { typeof(string), typeof(bool) },
            modifiers: null);

        Assert.NotNull(target);
    }

    [Fact]
    public void HarmonyPatchAttribute_PointsAtArchiveMission()
    {
        var attr = typeof(MissionArchivePatch)
            .GetCustomAttributes(typeof(HarmonyPatch), inherit: false)
            .Cast<HarmonyPatch>()
            .First();

        Assert.Equal(typeof(GamePlayer),                attr.info.declaringType);
        Assert.Equal(nameof(GamePlayer.ArchiveMission), attr.info.methodName);
    }

    [Fact]
    public void Postfix_IsStaticPrivate_WithHarmonyPostfix()
    {
        var postfix = typeof(MissionArchivePatch).GetMethod(
            "Postfix", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(postfix);
        Assert.NotNull(postfix!.GetCustomAttribute<HarmonyPostfix>());
    }

    // --- BuildSynthesizedCompleted dedup/clone behaviour ------------------

    [Fact]
    public void BuildSynthesizedCompleted_ClonesFieldsAndBumpsTypeAndOutcome()
    {
        var clock = new FakeClock { GameSeconds = 100.0 };
        var builder = new ActivityEventBuilder(clock, () => null);

        var accepted = TestEvents.Baseline(
            eventId: "accept-1",
            storyId: "m1",
            gameSeconds: 50.0,
            type: ActivityEventType.Accepted,
            missionType: MissionType.Bounty,
            sourceSystemId: "sys-zoran",
            sourceFaction:  "BountyGuild")
            with { RewardsCredits = null };

        clock.GameSeconds = 150.0;
        var synth = builder.BuildSynthesizedCompleted(accepted);

        Assert.Equal(ActivityEventType.Completed, synth.Type);
        Assert.Equal(Outcome.Completed,            synth.Outcome);
        Assert.Equal(150.0,                        synth.GameSeconds);
        Assert.Equal("m1",                         synth.StoryId);        // cloned
        Assert.Equal(MissionType.Bounty,           synth.MissionType);    // cloned
        Assert.Equal("sys-zoran",                  synth.SourceSystemId); // cloned
        Assert.Equal("BountyGuild",                synth.SourceFaction);  // cloned
        Assert.NotEqual(accepted.EventId,          synth.EventId);        // fresh
        // Rewards are null — we didn't see the transition that would populate them.
        Assert.Null(synth.RewardsCredits);
        Assert.Null(synth.RewardsExperience);
        Assert.Null(synth.RewardsReputation);
    }
}
