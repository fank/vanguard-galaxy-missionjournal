using System;
using VGMissionLog.Logging;

namespace VGMissionLog.Tests.Support;

/// <summary>
/// Test-only <see cref="IClock"/> with freely-set <c>GameSeconds</c> and
/// <c>UtcNow</c>. Lets Phase-4 patch tests assert deterministic timestamps
/// on emitted events ("when the clock is at t=123.45, the Accepted event
/// carries GameSeconds=123.45"), and advance time between logical steps
/// without any real sleeps.
/// </summary>
internal sealed class FakeClock : IClock
{
    public double GameSeconds { get; set; }
    public DateTime UtcNow     { get; set; } = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Advance the in-game clock by <paramref name="deltaSeconds"/>.</summary>
    public void AdvanceGame(double deltaSeconds) => GameSeconds += deltaSeconds;

    /// <summary>Advance real-time by <paramref name="delta"/>.</summary>
    public void AdvanceReal(TimeSpan delta) => UtcNow = UtcNow.Add(delta);
}
