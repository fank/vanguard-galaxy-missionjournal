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

    // --- Archive race: Complete's Prefix/Postfix dedup -------------------

    /// <summary>
    /// Regression for the real-save bug: Harmony postfix order on the chain
    /// CompleteMission → RemoveMission → ArchiveMission means Archive's
    /// postfix fires before Complete's. Without coordination, Archive's
    /// backstop sees "no Completed event yet" and synthesizes a duplicate.
    /// The fix: Complete's Prefix publishes the storyId to an in-flight
    /// set that Archive checks.
    /// </summary>
    [Fact]
    public void ArchiveDuringActiveCompletion_SkipsSynth()
    {
        var clock   = new FakeClock { GameSeconds = 100.0 };
        var builder = new ActivityEventBuilder(clock, () => null);
        var log     = new ActivityLog();
        log.Append(TestEvents.Baseline(eventId: "acc", storyId: "story-1",
                                        type: ActivityEventType.Accepted));

        // Wire patches to the same log/builder/logger.
        MissionCompletePatch.Builder = builder;
        MissionCompletePatch.Log     = log;
        MissionCompletePatch.BepLog  = new BepInEx.Logging.ManualLogSource("test");
        MissionArchivePatch.Builder  = builder;
        MissionArchivePatch.Log      = log;
        MissionArchivePatch.BepLog   = new BepInEx.Logging.ManualLogSource("test");
        MissionCompletePatch.InFlightStoryIds.Clear();

        // Simulate Harmony's actual ordering: Complete's Prefix runs first,
        // then the inner call chain runs (with Archive's Postfix firing as
        // RemoveMission returns), then Complete's Postfix.
        var mission = TestMission.Generic("story-1");
        InvokeCompletePrefix(mission);
        InvokeArchivePostfix("story-1");        // should be suppressed
        InvokeCompletePostfix(mission);          // emits the one true Completed

        var completedEvents = log.AllEvents
            .Where(e => e.Type == ActivityEventType.Completed
                     && e.StoryId == "story-1")
            .ToList();
        Assert.Single(completedEvents);
        // InFlight set is cleaned up so a later archive of the same id can
        // still trigger the legitimate backstop path.
        Assert.DoesNotContain("story-1", MissionCompletePatch.InFlightStoryIds);
    }

    [Fact]
    public void ArchiveOutsideActiveCompletion_StillSynths()
    {
        // The backstop's legit use-case: a path (dev cheat, swallowed
        // exception) that calls ArchiveMission without going through
        // CompleteMission. The storyId is not in the in-flight set, so we
        // do synth a Completed from the most recent prior event.
        var clock   = new FakeClock { GameSeconds = 100.0 };
        var builder = new ActivityEventBuilder(clock, () => null);
        var log     = new ActivityLog();
        log.Append(TestEvents.Baseline(eventId: "acc", storyId: "story-dev",
                                        type: ActivityEventType.Accepted));

        MissionArchivePatch.Builder = builder;
        MissionArchivePatch.Log     = log;
        MissionArchivePatch.BepLog  = new BepInEx.Logging.ManualLogSource("test");
        MissionCompletePatch.InFlightStoryIds.Clear();

        InvokeArchivePostfix("story-dev");

        Assert.Contains(log.AllEvents,
            e => e.Type == ActivityEventType.Completed && e.StoryId == "story-dev");
    }

    private static void InvokeCompletePrefix(Source.MissionSystem.Mission mission) =>
        typeof(MissionCompletePatch)
            .GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object?[] { mission });

    private static void InvokeCompletePostfix(Source.MissionSystem.Mission mission) =>
        typeof(MissionCompletePatch)
            .GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object?[] { mission });

    private static void InvokeArchivePostfix(string id) =>
        typeof(MissionArchivePatch)
            .GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object?[] { id });

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
            missionSubclass: "BountyMission",
            sourceSystemId: "sys-zoran",
            sourceFaction:  "BountyGuild")
            with { RewardsCredits = null };

        clock.GameSeconds = 150.0;
        var synth = builder.BuildSynthesizedCompleted(accepted);

        Assert.Equal(ActivityEventType.Completed, synth.Type);
        Assert.Equal(Outcome.Completed,            synth.Outcome);
        Assert.Equal(150.0,                        synth.GameSeconds);
        Assert.Equal("m1",                         synth.StoryId);           // cloned
        Assert.Equal("BountyMission",              synth.MissionSubclass);   // cloned
        Assert.Equal("sys-zoran",                  synth.SourceSystemId);    // cloned
        Assert.Equal("BountyGuild",                synth.SourceFaction);     // cloned
        Assert.NotEqual(accepted.EventId,          synth.EventId);           // fresh
        // Rewards are null — we didn't see the transition that would populate them.
        Assert.Null(synth.RewardsCredits);
        Assert.Null(synth.RewardsExperience);
        Assert.Null(synth.RewardsReputation);
    }
}
