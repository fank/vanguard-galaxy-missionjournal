using System.Collections.Generic;

namespace VGMissionJournal.Logging;

/// <summary>
/// Immutable description of one step in a mission's plan. See
/// <see cref="MissionRecord.Steps"/>.
/// </summary>
public sealed record MissionStepDefinition(
    string? Description,
    bool RequireAllObjectives,
    bool Hidden,
    IReadOnlyList<MissionObjectiveDefinition> Objectives);
