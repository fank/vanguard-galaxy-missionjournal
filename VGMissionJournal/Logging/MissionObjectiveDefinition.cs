using System.Collections.Generic;

namespace VGMissionJournal.Logging;

/// <summary>
/// Immutable description of one objective within a <see cref="MissionStepDefinition"/>.
/// <para>v3 captures <b>structure only</b>: the objective's type name
/// (<c>objective.GetType().Name</c>) and the stable fields known at mission
/// generation (e.g. <c>enemyFaction</c>, <c>targetCount</c>, <c>targetPOI</c>).
/// Dynamic progress (kill counters, status text) is not captured — once a mission
/// is generated, its objective structure never changes per vanilla's design.</para>
/// </summary>
public sealed record MissionObjectiveDefinition(
    string Type,
    IReadOnlyDictionary<string, object?>? Fields);
