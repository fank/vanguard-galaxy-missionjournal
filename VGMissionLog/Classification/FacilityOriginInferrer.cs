using Source.MissionSystem;
using VGMissionLog.Logging;

namespace VGMissionLog.Classification;

/// <summary>
/// Best-effort inference of where a mission was sourced, from its
/// concrete type alone. The three ladder-mission subclasses each have
/// dedicated boards in vanilla; <see cref="FacilityOrigin.Bar"/> /
/// <see cref="FacilityOrigin.MissionBoard"/> / broker origins need the
/// Phase-4 offer hooks to populate and are returned as <c>null</c> here.
/// </summary>
internal static class FacilityOriginInferrer
{
    public static FacilityOrigin? Infer(Mission mission) =>
        mission switch
        {
            BountyMission   => FacilityOrigin.BountyBoard,
            PatrolMission   => FacilityOrigin.PoliceBoard,
            IndustryMission => FacilityOrigin.IndustryBoard,
            _               => null,
        };
}
