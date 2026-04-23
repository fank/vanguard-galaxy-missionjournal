using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Source.Item;
using Source.MissionSystem;
using Source.MissionSystem.Objectives;

namespace VGMissionLog.Tests.Support;

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
/// zeroed (null for refs, default for values). That's enough for
/// <see cref="Classification.MissionClassifier"/> pattern-match assertions,
/// which only inspect the concrete type and the <c>storyId</c> field.
///
/// In production, vanilla constructs Mission instances properly; our
/// Harmony postfixes receive them as fully-populated parameters. This
/// helper exists only so Phase-2 classifier tests can isolate the typing
/// logic from vanilla's Unity-side initialization.
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

    private static void SetBackingField(System.Type declaringType, object instance, string propertyName, object? value)
    {
        // C# auto-property backing fields are named `<PropertyName>k__BackingField`.
        var field = declaringType.GetField(
            $"<{propertyName}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        field!.SetValue(instance, value);
    }
}
