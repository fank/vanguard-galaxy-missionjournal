using System.Linq;
using System.Reflection;
using HarmonyLib;
using Source.Player;
using VGMissionJournal.Logging;
using VGMissionJournal.Patches;
using VGMissionJournal.Tests.Support;
using Xunit;

namespace VGMissionJournal.Tests.Patches;

[Collection("PatchStatics")]
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

    // --- Archive race: Complete's Prefix/Postfix dedup -------------------

    /// <summary>
    /// Regression for the real-save bug: Harmony postfix order on the chain
    /// CompleteMission → RemoveMission → ArchiveMission means Archive's
    /// postfix fires before Complete's. Without coordination, Archive's
    /// backstop sees "no Completed transition yet" and synthesizes a
    /// duplicate. The fix: Complete's Prefix publishes the storyId to an
    /// in-flight set that Archive checks.
    /// </summary>
    [Fact]
    public void ArchiveDuringActiveCompletion_SkipsSynth()
    {
        var clock   = new FakeClock { GameSeconds = 100.0 };
        var builder = new MissionRecordBuilder(clock, () => null);
        var store   = new MissionStore();
        var bepLog  = new BepInEx.Logging.ManualLogSource("test");

        // Seed the store with an accepted mission carrying storyId="story-1".
        var mission = TestMission.Generic("story-1");
        var accepted = builder.CreateFromAccept(mission);
        store.Upsert(accepted);

        // Wire both patches to the same builder/store/logger.
        MissionCompletePatch.Builder = builder;
        MissionCompletePatch.Store   = store;
        MissionCompletePatch.BepLog  = bepLog;
        MissionArchivePatch.Builder  = builder;
        MissionArchivePatch.Store    = store;
        MissionArchivePatch.BepLog   = bepLog;
        MissionCompletePatch.InFlightStoryIds.Clear();

        // Simulate Harmony's actual ordering: Complete's Prefix runs first,
        // then the inner call chain runs (with Archive's Postfix firing as
        // RemoveMission returns), then Complete's Postfix.
        InvokeCompletePrefix(mission);
        InvokeArchivePostfix("story-1");        // should be suppressed
        InvokeCompletePostfix(mission);          // appends the one true Completed

        // Exactly one record, and its timeline should have exactly one
        // Completed entry (no duplicate synth from Archive).
        var r = store.GetByInstanceId(accepted.MissionInstanceId);
        Assert.NotNull(r);
        var completedEntries = r!.Timeline
            .Where(e => e.State == TimelineState.Completed)
            .ToList();
        Assert.Single(completedEntries);
        Assert.Equal(Outcome.Completed, r.Outcome);

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
        // do synth a Completed transition on the active record.
        var clock   = new FakeClock { GameSeconds = 100.0 };
        var builder = new MissionRecordBuilder(clock, () => null);
        var store   = new MissionStore();
        var bepLog  = new BepInEx.Logging.ManualLogSource("test");

        var mission = TestMission.Generic("story-dev");
        var accepted = builder.CreateFromAccept(mission);
        store.Upsert(accepted);

        MissionArchivePatch.Builder = builder;
        MissionArchivePatch.Store   = store;
        MissionArchivePatch.BepLog  = bepLog;
        MissionCompletePatch.InFlightStoryIds.Clear();

        clock.GameSeconds = 150.0;
        InvokeArchivePostfix("story-dev");

        var r = store.GetByInstanceId(accepted.MissionInstanceId);
        Assert.NotNull(r);
        Assert.Equal(Outcome.Completed, r!.Outcome);
        Assert.False(r.IsActive);
        Assert.Equal(150.0, r.TerminalAtGameSeconds);
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
}
