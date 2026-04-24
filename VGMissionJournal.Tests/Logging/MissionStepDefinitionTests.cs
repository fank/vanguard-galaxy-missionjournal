using System.Collections.Generic;
using VGMissionJournal.Logging;
using Xunit;

namespace VGMissionJournal.Tests.Logging;

public class MissionStepDefinitionTests
{
    [Fact]
    public void ObjectiveDefinition_CarriesTypeAndOptionalFields()
    {
        var obj = new MissionObjectiveDefinition(
            "KillEnemies",
            new Dictionary<string, object?> { ["enemyFaction"] = "CrimsonFang", ["targetCount"] = 3 });

        Assert.Equal("KillEnemies",   obj.Type);
        Assert.Equal("CrimsonFang",   obj.Fields!["enemyFaction"]);
        Assert.Equal(3,               obj.Fields["targetCount"]);
    }

    [Fact]
    public void ObjectiveDefinition_FieldsOptional_NullAllowed()
    {
        var obj = new MissionObjectiveDefinition("TravelToPOI", Fields: null);
        Assert.Null(obj.Fields);
    }

    [Fact]
    public void StepDefinition_HoldsDescriptionFlagsAndObjectives()
    {
        var step = new MissionStepDefinition(
            Description: "Eliminate the raiders",
            RequireAllObjectives: true,
            Hidden: false,
            Objectives: new[]
            {
                new MissionObjectiveDefinition("KillEnemies", null),
            });

        Assert.Equal("Eliminate the raiders", step.Description);
        Assert.True(step.RequireAllObjectives);
        Assert.False(step.Hidden);
        Assert.Single(step.Objectives);
    }
}
