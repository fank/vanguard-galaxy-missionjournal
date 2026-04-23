using System.Runtime.CompilerServices;
using Source.MissionSystem;

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
}
