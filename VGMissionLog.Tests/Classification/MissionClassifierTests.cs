using VGMissionLog.Classification;
using VGMissionLog.Logging;
using VGMissionLog.Tests.Support;
using Xunit;

namespace VGMissionLog.Tests.Classification;

public class MissionClassifierTests
{
    [Fact]
    public void BountyMission_Classifies_AsBounty()
    {
        Assert.Equal(MissionType.Bounty, MissionClassifier.Classify(TestMission.Bounty()));
    }

    [Fact]
    public void PatrolMission_Classifies_AsPatrol()
    {
        Assert.Equal(MissionType.Patrol, MissionClassifier.Classify(TestMission.Patrol()));
    }

    [Fact]
    public void IndustryMission_Classifies_AsIndustry()
    {
        Assert.Equal(MissionType.Industry, MissionClassifier.Classify(TestMission.Industry()));
    }

    [Fact]
    public void MissionWithStoryId_NoThirdPartyPrefix_ClassifiesAsStory()
    {
        // Vanilla story arcs register a storyId like "tutorial_1" without the
        // _llm_ infix; they should land in Story, not Generic.
        Assert.Equal(MissionType.Story, MissionClassifier.Classify(TestMission.Generic("tutorial_1")));
    }

    [Fact]
    public void MissionWithVGAnimaStoryId_ClassifiesAsThirdPartyVganima()
    {
        Assert.Equal(
            MissionType.ThirdParty("vganima"),
            MissionClassifier.Classify(TestMission.Generic("vganima_llm_abc123")));
    }

    [Fact]
    public void MissionWithOtherModStoryId_ExtractsTheirPrefix()
    {
        Assert.Equal(
            MissionType.ThirdParty("othermod"),
            MissionClassifier.Classify(TestMission.Generic("othermod_llm_xyz")));
    }

    [Fact]
    public void MissionWithoutStoryId_ClassifiesAsGeneric()
    {
        Assert.Equal(MissionType.Generic, MissionClassifier.Classify(TestMission.Generic()));
    }

    [Fact]
    public void MissionWithEmptyStoryId_ClassifiesAsGeneric()
    {
        Assert.Equal(MissionType.Generic, MissionClassifier.Classify(TestMission.Generic("")));
    }

    [Fact]
    public void SubclassMatchWins_EvenWhenStoryIdIsPresent()
    {
        // A BountyMission carrying a storyId (mod-generated variant) still
        // classifies by its concrete runtime type, not by the storyId.
        Assert.Equal(
            MissionType.Bounty,
            MissionClassifier.Classify(TestMission.Bounty("some_story_id")));
    }

    [Fact]
    public void SubclassName_ReturnsConcreteTypeName()
    {
        Assert.Equal("BountyMission",   MissionClassifier.SubclassName(TestMission.Bounty()));
        Assert.Equal("PatrolMission",   MissionClassifier.SubclassName(TestMission.Patrol()));
        Assert.Equal("IndustryMission", MissionClassifier.SubclassName(TestMission.Industry()));
        Assert.Equal("Mission",         MissionClassifier.SubclassName(TestMission.Generic()));
    }
}
