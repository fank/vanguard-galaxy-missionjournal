namespace VGMissionLog.Logging;

/// <summary>
/// Best-effort inferred activity shape of a mission. Null when the mission's
/// step objectives don't match a known pattern. Spec R1.2 / arch doc §
/// Classification.
/// </summary>
public enum ActivityArchetype
{
    Combat,
    Gather,
    Salvage,
    Deliver,
    Escort,
    Other,
}
