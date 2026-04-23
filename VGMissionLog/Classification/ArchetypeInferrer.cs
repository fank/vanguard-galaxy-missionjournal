using System.Collections.Generic;
using System.Reflection;
using Source.Item;
using Source.MissionSystem;
using Source.MissionSystem.Objectives;
using VGMissionLog.Logging;

namespace VGMissionLog.Classification;

/// <summary>
/// Best-effort archetype inference from a <see cref="Mission"/>'s step /
/// objective list. Returns <c>null</c> for unclassifiable missions —
/// ambiguity is fine; consumers treat null as "unknown" rather than
/// forcing a bucket.
///
/// Heuristic order (most-specific first; earlier rules short-circuit):
/// <list type="number">
///   <item>Any <c>KillEnemies</c>       → <see cref="ActivityArchetype.Combat"/>.</item>
///   <item>Any <c>ProtectUnit</c>       → <see cref="ActivityArchetype.Escort"/>.</item>
///   <item>Any <c>CollectItemTypes</c> whose <c>itemCategory</c> is
///         <c>Ore</c>/<c>RefinedProduct</c> → <see cref="ActivityArchetype.Gather"/>;
///         <c>Salvage</c>/<c>Junk</c>   → <see cref="ActivityArchetype.Salvage"/>.</item>
///   <item>Only <c>TravelToPOI</c>       → <see cref="ActivityArchetype.Deliver"/>.</item>
///   <item>Otherwise                    → <c>null</c>.</item>
/// </list>
///
/// <para>A mission with both a KillEnemies AND a TravelToPOI is Combat,
/// not Deliver — "Travel then kill" reads as combat from the player's POV.</para>
///
/// <para>Implementation note: steps / objectives are read via the
/// compiler-synthesised backing fields rather than the public auto-
/// property getters. The publicized Assembly-CSharp stub we compile
/// against has <c>throw null;</c> IL bodies for all methods including
/// property getters, so <c>mission.steps</c> NREs inside xUnit. Field
/// reads work in both the stub (the backing field is real) and the game
/// runtime (the real DLL's field is identical) and avoid the divergence
/// entirely. The reflection cost is negligible — inference runs once
/// per mission lifecycle event.</para>
/// </summary>
internal static class ArchetypeInferrer
{
    private static readonly FieldInfo _missionStepsField =
        typeof(Mission).GetField("<steps>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new System.InvalidOperationException(
            "Mission.<steps>k__BackingField not found — vanilla layout changed?");

    private static readonly FieldInfo _stepObjectivesField =
        typeof(MissionStep).GetField("<objectives>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new System.InvalidOperationException(
            "MissionStep.<objectives>k__BackingField not found — vanilla layout changed?");

    public static ActivityArchetype? Infer(Mission mission)
    {
        var objectives = FlattenObjectives(mission);
        if (objectives.Count == 0) return null;

        foreach (var obj in objectives)
            if (obj is KillEnemies) return ActivityArchetype.Combat;

        foreach (var obj in objectives)
            if (obj is ProtectUnit) return ActivityArchetype.Escort;

        foreach (var obj in objectives)
        {
            if (obj is not CollectItemTypes cit || !cit.itemCategory.HasValue) continue;
            var cat = cit.itemCategory.Value;
            if (cat == ItemCategory.Ore || cat == ItemCategory.RefinedProduct)
                return ActivityArchetype.Gather;
            if (cat == ItemCategory.Salvage || cat == ItemCategory.Junk)
                return ActivityArchetype.Salvage;
        }

        // "Alone" means every objective in every step is a TravelToPOI.
        var allTravel = true;
        foreach (var obj in objectives)
        {
            if (obj is not TravelToPOI) { allTravel = false; break; }
        }
        if (allTravel) return ActivityArchetype.Deliver;

        return null;
    }

    private static List<MissionObjective> FlattenObjectives(Mission mission)
    {
        var result = new List<MissionObjective>();
        if (mission is null) return result;

        var steps = _missionStepsField.GetValue(mission) as List<MissionStep>;
        if (steps is null) return result;

        foreach (var step in steps)
        {
            if (step is null) continue;
            var objectives = _stepObjectivesField.GetValue(step) as List<MissionObjective>;
            if (objectives is null) continue;
            foreach (var obj in objectives)
            {
                if (obj != null) result.Add(obj);
            }
        }
        return result;
    }
}
