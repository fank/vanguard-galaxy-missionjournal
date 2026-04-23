using System;

namespace VGMissionLog.Logging;

/// <summary>
/// Time source injected into every <see cref="ActivityEvent"/> emission
/// site (Phase-4 Harmony patches) so production code can read vanilla's
/// in-game clock while tests inject a deterministic fake.
///
/// Two readings per call: the in-game elapsed-seconds accumulator (used
/// for age/window queries — <c>GameSeconds</c>-based windows survive real
/// pauses and fast-forwards) and real UTC (used for wall-clock debugging
/// and sidecar quarantine filenames).
/// </summary>
internal interface IClock
{
    double GameSeconds { get; }
    DateTime UtcNow { get; }
}
