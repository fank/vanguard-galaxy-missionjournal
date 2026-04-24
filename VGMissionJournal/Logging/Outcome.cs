namespace VGMissionJournal.Logging;

/// <summary>
/// Terminal outcome of a mission. Null on non-terminal events (Offered,
/// Accepted, ObjectiveProgressed). Spec R1.2.
/// </summary>
public enum Outcome
{
    Completed,
    Failed,
    Abandoned,
}
