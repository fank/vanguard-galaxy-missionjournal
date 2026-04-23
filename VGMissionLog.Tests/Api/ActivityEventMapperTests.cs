using System.Collections.Generic;
using VGMissionLog.Api;
using VGMissionLog.Logging;
using VGMissionLog.Tests.Support;
using Xunit;

namespace VGMissionLog.Tests.Api;

public class ActivityEventMapperTests
{
    [Fact]
    public void ToDict_NoSteps_OmitsStepsKey()
    {
        var evt  = TestEvents.Baseline();
        var dict = ActivityEventMapper.ToDict(evt);

        Assert.False(dict.ContainsKey("steps"));
    }

    [Fact]
    public void ToDict_WithSteps_EmitsStepsArrayWithObjectives()
    {
        var objective = new MissionObjectiveSnapshot(
            Type:       "KillEnemies",
            IsComplete: false,
            StatusText: "Kill 0/5 Pirates",
            Fields:     new Dictionary<string, object?>
            {
                ["requiredAmount"] = 5,
                ["shipType"]       = "Pirate",
            });
        var step = new MissionStepSnapshot(
            Description:          "Eliminate the pirates",
            IsComplete:           false,
            RequireAllObjectives: true,
            Hidden:               false,
            Objectives:           new[] { objective });

        var evt = TestEvents.Baseline() with { Steps = new[] { step } };
        var dict = ActivityEventMapper.ToDict(evt);

        Assert.True(dict.ContainsKey("steps"));
        var steps = (IReadOnlyList<IReadOnlyDictionary<string, object?>>)dict["steps"]!;
        Assert.Single(steps);
        var s0 = steps[0];
        Assert.Equal("Eliminate the pirates", s0["description"]);
        Assert.Equal(false, s0["isComplete"]);
        Assert.Equal(true,  s0["requireAllObjectives"]);

        var objectives = (IReadOnlyList<IReadOnlyDictionary<string, object?>>)s0["objectives"]!;
        Assert.Single(objectives);
        var o0 = objectives[0];
        Assert.Equal("KillEnemies",         o0["type"]);
        Assert.Equal(false,                 o0["isComplete"]);
        Assert.Equal("Kill 0/5 Pirates",    o0["statusText"]);
        var fields = (IReadOnlyDictionary<string, object?>)o0["fields"]!;
        Assert.Equal(5,        fields["requiredAmount"]);
        Assert.Equal("Pirate", fields["shipType"]);
    }

    [Fact]
    public void ToDict_ObjectiveWithoutStatusText_OmitsKey()
    {
        var objective = new MissionObjectiveSnapshot(
            Type:       "TravelToPOI",
            IsComplete: true,
            StatusText: null,
            Fields:     null);
        var step = new MissionStepSnapshot(
            Description:          null,
            IsComplete:           true,
            RequireAllObjectives: true,
            Hidden:               false,
            Objectives:           new[] { objective });

        var evt = TestEvents.Baseline() with { Steps = new[] { step } };
        var dict = ActivityEventMapper.ToDict(evt);

        var steps = (IReadOnlyList<IReadOnlyDictionary<string, object?>>)dict["steps"]!;
        var s0 = steps[0];
        Assert.False(s0.ContainsKey("description"));
        var o0 = ((IReadOnlyList<IReadOnlyDictionary<string, object?>>)s0["objectives"]!)[0];
        Assert.False(o0.ContainsKey("statusText"));
        Assert.False(o0.ContainsKey("fields"));
    }
}
