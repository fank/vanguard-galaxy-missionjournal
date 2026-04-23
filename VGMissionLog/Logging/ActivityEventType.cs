namespace VGMissionLog.Logging;

/// <summary>
/// Lifecycle transition kinds for a mission. Each <see cref="ActivityEvent"/>
/// carries exactly one. See spec R1.1.
/// </summary>
public enum ActivityEventType
{
    Offered,
    Accepted,
    Completed,
    Failed,
    Abandoned,
    ObjectiveProgressed,
}
