namespace VGMissionJournal.Logging;

/// <summary>
/// One transition in a mission's timeline. <see cref="RealUtc"/> is optional —
/// we stamp it on Accepted and terminal entries (the anchors consumers use for
/// wall-clock reasoning); interior transitions, if any are ever added, may omit it.
/// </summary>
public sealed record TimelineEntry(
    TimelineState State,
    double GameSeconds,
    string? RealUtc)
{
    public bool IsTerminal =>
        State is TimelineState.Completed or TimelineState.Failed or TimelineState.Abandoned;
}
