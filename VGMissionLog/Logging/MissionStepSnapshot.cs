using System.Collections.Generic;

namespace VGMissionLog.Logging;

/// <summary>
/// Snapshot of a single <c>MissionStep</c> captured at an event's
/// lifecycle transition. See <see cref="ActivityEvent.Steps"/>.
///
/// <para><b>Fields:</b>
/// <list type="bullet">
///   <item><c>Description</c> — the step's display description
///         (vanilla's user-visible text). Null when the step has none.</item>
///   <item><c>IsComplete</c> — read from <c>MissionStep.isComplete</c>,
///         which evaluates each objective's <c>IsComplete()</c>.</item>
///   <item><c>RequireAllObjectives</c> — when true, every objective must
///         complete; when false, any one completes the step.</item>
///   <item><c>Hidden</c> — vanilla flag for steps the UI hides (branch
///         stubs, guards, etc.). Consumers usually skip these.</item>
///   <item><c>Objectives</c> — the raw objective snapshots; see
///         <see cref="MissionObjectiveSnapshot"/>.</item>
/// </list></para>
/// </summary>
public sealed record MissionStepSnapshot(
    string? Description,
    bool IsComplete,
    bool RequireAllObjectives,
    bool Hidden,
    IReadOnlyList<MissionObjectiveSnapshot> Objectives);
