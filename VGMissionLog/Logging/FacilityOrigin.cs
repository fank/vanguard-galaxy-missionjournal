namespace VGMissionLog.Logging;

/// <summary>
/// Where the mission was sourced — bar salesman, board, broker, etc.
/// Best-effort inference in MVP (spec R1.2): BountyBoard / PoliceBoard /
/// IndustryBoard fall out of the Mission subclass, while Bar / MissionBoard
/// require the Phase-4 offer hooks to populate reliably.
/// </summary>
public enum FacilityOrigin
{
    Bar,
    MissionBoard,
    BountyBoard,
    PoliceBoard,
    IndustryBoard,
    Other,
}
