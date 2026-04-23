using VGMissionLog.Classification;
using VGMissionLog.Logging;
using VGMissionLog.Tests.Support;
using Xunit;

namespace VGMissionLog.Tests.Classification;

public class FacilityOriginInferrerTests
{
    [Fact]
    public void Bounty_MapsTo_BountyBoard()
    {
        Assert.Equal(FacilityOrigin.BountyBoard,
            FacilityOriginInferrer.Infer(TestMission.Bounty()));
    }

    [Fact]
    public void Patrol_MapsTo_PoliceBoard()
    {
        Assert.Equal(FacilityOrigin.PoliceBoard,
            FacilityOriginInferrer.Infer(TestMission.Patrol()));
    }

    [Fact]
    public void Industry_MapsTo_IndustryBoard()
    {
        Assert.Equal(FacilityOrigin.IndustryBoard,
            FacilityOriginInferrer.Infer(TestMission.Industry()));
    }

    [Fact]
    public void Generic_MapsTo_Null_BarOriginComesFromPhase4OfferHook()
    {
        Assert.Null(FacilityOriginInferrer.Infer(TestMission.Generic()));
    }

    [Fact]
    public void GenericWithStoryId_StillReturnsNull()
    {
        // Story / ThirdParty classification doesn't carry facility origin —
        // those missions are accepted via brokers (bar / custom UI) which
        // the offer hooks (ML-T4h) resolve.
        Assert.Null(FacilityOriginInferrer.Infer(TestMission.Generic("vganima_llm_abc")));
    }
}
