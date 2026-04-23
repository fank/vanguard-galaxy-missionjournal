using Source.Item;
using VGMissionLog.Classification;
using VGMissionLog.Logging;
using VGMissionLog.Tests.Support;
using Xunit;

namespace VGMissionLog.Tests.Classification;

public class ArchetypeInferrerTests
{
    [Fact]
    public void Infer_NoSteps_ReturnsNull()
    {
        Assert.Null(ArchetypeInferrer.Infer(TestMission.Generic()));
    }

    [Fact]
    public void Infer_NoObjectives_ReturnsNull()
    {
        var mission = TestMission.WithObjectives();

        Assert.Null(ArchetypeInferrer.Infer(mission));
    }

    [Fact]
    public void Infer_KillEnemies_ReturnsCombat()
    {
        var mission = TestMission.WithObjectives(TestMission.Kill());

        Assert.Equal(ActivityArchetype.Combat, ArchetypeInferrer.Infer(mission));
    }

    [Fact]
    public void Infer_ProtectUnit_ReturnsEscort()
    {
        var mission = TestMission.WithObjectives(TestMission.Protect());

        Assert.Equal(ActivityArchetype.Escort, ArchetypeInferrer.Infer(mission));
    }

    [Fact]
    public void Infer_CollectOre_ReturnsGather()
    {
        var mission = TestMission.WithObjectives(TestMission.Collect(ItemCategory.Ore));

        Assert.Equal(ActivityArchetype.Gather, ArchetypeInferrer.Infer(mission));
    }

    [Fact]
    public void Infer_CollectRefinedProduct_ReturnsGather()
    {
        var mission = TestMission.WithObjectives(TestMission.Collect(ItemCategory.RefinedProduct));

        Assert.Equal(ActivityArchetype.Gather, ArchetypeInferrer.Infer(mission));
    }

    [Fact]
    public void Infer_CollectSalvage_ReturnsSalvage()
    {
        var mission = TestMission.WithObjectives(TestMission.Collect(ItemCategory.Salvage));

        Assert.Equal(ActivityArchetype.Salvage, ArchetypeInferrer.Infer(mission));
    }

    [Fact]
    public void Infer_CollectJunk_ReturnsSalvage()
    {
        var mission = TestMission.WithObjectives(TestMission.Collect(ItemCategory.Junk));

        Assert.Equal(ActivityArchetype.Salvage, ArchetypeInferrer.Infer(mission));
    }

    [Fact]
    public void Infer_CollectOther_ItemCategory_ReturnsNull()
    {
        // Ammo / Module / Turret etc. don't map to Gather or Salvage.
        var mission = TestMission.WithObjectives(TestMission.Collect(ItemCategory.Ammo));

        Assert.Null(ArchetypeInferrer.Infer(mission));
    }

    [Fact]
    public void Infer_CollectItemTypes_NoItemCategory_ReturnsNull()
    {
        // Missions that don't pin down an item category fall through.
        var mission = TestMission.WithObjectives(TestMission.Collect(category: null));

        Assert.Null(ArchetypeInferrer.Infer(mission));
    }

    [Fact]
    public void Infer_OnlyTravelToPOI_ReturnsDeliver()
    {
        var mission = TestMission.WithObjectives(TestMission.Travel());

        Assert.Equal(ActivityArchetype.Deliver, ArchetypeInferrer.Infer(mission));
    }

    [Fact]
    public void Infer_TravelPlusKillEnemies_Combat_TakesPrecedence()
    {
        // "Travel to POI, then kill" reads as combat from the player's POV.
        var mission = TestMission.WithObjectives(TestMission.Travel(), TestMission.Kill());

        Assert.Equal(ActivityArchetype.Combat, ArchetypeInferrer.Infer(mission));
    }

    [Fact]
    public void Infer_KillPlusProtect_Combat_Wins()
    {
        // Combat precedence over Escort — assault missions with protected
        // objects are fundamentally combat.
        var mission = TestMission.WithObjectives(TestMission.Kill(), TestMission.Protect());

        Assert.Equal(ActivityArchetype.Combat, ArchetypeInferrer.Infer(mission));
    }

    [Fact]
    public void Infer_MultipleSteps_FlattensObjectives()
    {
        // Step 1: Travel; Step 2: Collect salvage. Should classify as Salvage.
        var mission = TestMission.WithSteps(
            TestMission.BuildStep(TestMission.Travel()),
            TestMission.BuildStep(TestMission.Collect(ItemCategory.Salvage)));

        Assert.Equal(ActivityArchetype.Salvage, ArchetypeInferrer.Infer(mission));
    }

    [Fact]
    public void Infer_UnknownObjectiveType_ReturnsNull()
    {
        // Mining is a real vanilla objective we don't classify.
        var mission = TestMission.WithObjectives(TestMission.Mine());

        Assert.Null(ArchetypeInferrer.Infer(mission));
    }
}
