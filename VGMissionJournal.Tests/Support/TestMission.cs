using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Source.Galaxy;
using Source.Item;
using Source.MissionSystem;
using Source.MissionSystem.Objectives;
using Source.MissionSystem.Rewards;

namespace VGMissionJournal.Tests.Support;

/// <summary>
/// Construction helpers for vanilla <see cref="Mission"/> subclasses in a
/// non-Unity test runtime.
///
/// Why not <c>new BountyMission()</c>? Vanilla's instance ctors transit
/// through field initializers that resolve <c>InventoryItemType</c>,
/// <c>MissionStep</c>, <c>MissionReward</c>, and (for <c>PatrolMission</c>)
/// static <c>Faction.gold</c> etc. — several of those walk reflection
/// paths (<c>Type.GetType("Source.Galaxy.Factions.Gold")</c> →
/// <c>Gold()</c> → <c>HexToColor</c>) that rely on the Unity runtime
/// being live. Running them in xUnit NREs.
///
/// <see cref="RuntimeHelpers.GetUninitializedObject"/> skips the instance
/// ctor entirely, yielding a runtime-typed bare instance with all fields
/// zeroed (null for refs, default for values). That's enough for the
/// builder's pattern-match and field-read paths, which only inspect the
/// concrete type, <c>storyId</c>, and a handful of public/backing fields.
///
/// In production, vanilla constructs Mission instances properly; our
/// Harmony postfixes receive them as fully-populated parameters. This
/// helper exists only so the builder tests can isolate the wiring logic
/// from vanilla's Unity-side initialization.
/// </summary>
internal static class TestMission
{
    public static Mission          Generic(string? storyId = null)  => Build<Mission>(storyId);
    public static BountyMission    Bounty(string? storyId = null)   => Build<BountyMission>(storyId);
    public static PatrolMission    Patrol(string? storyId = null)   => Build<PatrolMission>(storyId);
    public static IndustryMission  Industry(string? storyId = null) => Build<IndustryMission>(storyId);

    private static T Build<T>(string? storyId) where T : Mission
    {
        var mission = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        if (storyId != null)
        {
            typeof(Mission)
                .GetField(nameof(Mission.storyId))!
                .SetValue(mission, storyId);
        }
        return mission;
    }

    /// <summary>
    /// Populate a mission's <c>steps</c> with a single step containing the
    /// provided objectives. Used by archetype-inference tests.
    /// Mission.steps is a get-only auto-property (private set), so we reach
    /// through its compiler-synthesised backing field.
    /// </summary>
    public static Mission WithObjectives(params MissionObjective[] objectives)
    {
        var mission = Generic();
        SetBackingField(typeof(Mission), mission, "steps",
            new List<MissionStep> { BuildStep(objectives) });
        return mission;
    }

    /// <summary>Populate a mission's steps directly with a list of
    /// pre-built steps (useful when the test wants multiple steps).</summary>
    public static Mission WithSteps(params MissionStep[] steps)
    {
        var mission = Generic();
        SetBackingField(typeof(Mission), mission, "steps", new List<MissionStep>(steps));
        return mission;
    }

    public static MissionStep BuildStep(params MissionObjective[] objectives)
    {
        var step = (MissionStep)RuntimeHelpers.GetUninitializedObject(typeof(MissionStep));
        SetBackingField(typeof(MissionStep), step, "objectives",
            new List<MissionObjective>(objectives));
        return step;
    }

    // --- Objective factories (vanilla instance ctors NRE outside Unity;
    // bare instances are sufficient for pattern-match + field-read tests) ---

    public static KillEnemies   Kill()    => Uninit<KillEnemies>();
    public static ProtectUnit   Protect() => Uninit<ProtectUnit>();
    public static TravelToPOI   Travel()  => Uninit<TravelToPOI>();
    public static Mining        Mine()    => Uninit<Mining>();

    public static CollectItemTypes Collect(ItemCategory? category)
    {
        var c = Uninit<CollectItemTypes>();
        typeof(CollectItemTypes)
            .GetField(nameof(CollectItemTypes.itemCategory))!
            .SetValue(c, category);
        return c;
    }

    private static T Uninit<T>() where T : MissionObjective =>
        (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    // --- Reward factories ---------------------------------------------------
    //
    // The MissionReward subclasses have simple public fields (amount,
    // faction) but their ctors go through the same publicized-stub
    // `throw null;` IL as Mission subclasses, so we reach for
    // GetUninitializedObject. Fields are set by assigning to the bare
    // instance since they're public.

    public static Credits Credits(int amount)
    {
        var r = (Credits)RuntimeHelpers.GetUninitializedObject(typeof(Credits));
        r.amount = amount;
        return r;
    }

    public static Experience Experience(int amount)
    {
        var r = (Experience)RuntimeHelpers.GetUninitializedObject(typeof(Experience));
        r.amount = amount;
        return r;
    }

    public static Source.MissionSystem.Rewards.Skillpoint Skillpoint(int amount)
    {
        var r = (Source.MissionSystem.Rewards.Skillpoint)RuntimeHelpers.GetUninitializedObject(
            typeof(Source.MissionSystem.Rewards.Skillpoint));
        r.amount = amount;
        return r;
    }

    public static Source.MissionSystem.Rewards.Skilltree Skilltree(string name)
    {
        var r = (Source.MissionSystem.Rewards.Skilltree)RuntimeHelpers.GetUninitializedObject(
            typeof(Source.MissionSystem.Rewards.Skilltree));
        r.treeName = name;
        return r;
    }

    public static Source.MissionSystem.Rewards.StoryMission StoryMissionReward(string missionId)
    {
        var r = (Source.MissionSystem.Rewards.StoryMission)RuntimeHelpers.GetUninitializedObject(
            typeof(Source.MissionSystem.Rewards.StoryMission));
        r.missionId = missionId;
        return r;
    }

    /// <summary>
    /// Build a Reputation reward with a null faction — we can't construct
    /// a real Faction in xUnit because <c>Faction</c>'s static constructor
    /// self-references <c>Source.Galaxy.Factions.Gold/Red/Blue/…</c>,
    /// whose ctors NRE in the publicized-stub / no-Unity-runtime
    /// environment. Builder tests assert the "unknown" fallback when
    /// <c>rep.faction</c> is null; full rep-extraction coverage lives in
    /// ML-T7b's in-game manual E2E.
    /// </summary>
    public static Source.MissionSystem.Rewards.Reputation Reputation(int amount)
    {
        var rep = (Source.MissionSystem.Rewards.Reputation)RuntimeHelpers.GetUninitializedObject(
            typeof(Source.MissionSystem.Rewards.Reputation));
        rep.amount = amount;
        // rep.faction stays null; see doc comment above.
        return rep;
    }

    /// <summary>Populate a mission's <c>rewards</c> with a list of the
    /// given MissionReward instances. Same backing-field pattern as
    /// WithObjectives / WithSteps.</summary>
    public static Mission WithRewards(this Mission mission, params MissionReward[] rewards)
    {
        SetBackingField(typeof(Mission), mission, "rewards",
            new List<MissionReward>(rewards));
        return mission;
    }

    /// <summary>Shortcut: Generic + rewards for Complete-event tests.</summary>
    public static Mission GenericWithRewards(params MissionReward[] rewards) =>
        Generic().WithRewards(rewards);

    private static void SetBackingField(System.Type declaringType, object instance, string propertyName, object? value)
    {
        // C# auto-property backing fields are named `<PropertyName>k__BackingField`.
        var field = declaringType.GetField(
            $"<{propertyName}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        field!.SetValue(instance, value);
    }
}
