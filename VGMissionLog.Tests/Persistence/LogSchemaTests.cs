using Newtonsoft.Json;
using VGMissionLog.Logging;
using VGMissionLog.Persistence;
using VGMissionLog.Tests.Support;
using Xunit;

namespace VGMissionLog.Tests.Persistence;

public class LogSchemaTests
{
    [Fact]
    public void CurrentVersion_IsOne()
    {
        Assert.Equal(1, LogSchema.CurrentVersion);
    }

    [Fact]
    public void EmptySchema_RoundTripsThroughNewtonsoft()
    {
        var schema = new LogSchema(LogSchema.CurrentVersion, System.Array.Empty<ActivityEvent>());

        var json   = JsonConvert.SerializeObject(schema, LogSchema.SerializerSettings);
        var back   = JsonConvert.DeserializeObject<LogSchema>(json, LogSchema.SerializerSettings);

        Assert.NotNull(back);
        Assert.Equal(LogSchema.CurrentVersion, back!.Version);
        Assert.Empty(back.Events);
    }

    [Fact]
    public void SingleEvent_RoundTripsPrimitiveFields()
    {
        var original = TestEvents.Baseline(
            eventId: "abc",
            storyId: "m1",
            gameSeconds: 123.45,
            missionSubclass: "BountyMission",
            sourceSystemId: "sys-zoran",
            sourceFaction:  "BountyGuild")
            with { Outcome = Outcome.Completed, RewardsCredits = 5000 };

        var schema = new LogSchema(LogSchema.CurrentVersion, new[] { original });

        var json = JsonConvert.SerializeObject(schema, LogSchema.SerializerSettings);
        var back = JsonConvert.DeserializeObject<LogSchema>(json, LogSchema.SerializerSettings)!;

        Assert.Single(back.Events);
        var roundtripped = back.Events[0];
        Assert.Equal("abc",              roundtripped.EventId);
        Assert.Equal("m1",               roundtripped.StoryId);
        Assert.Equal(123.45,             roundtripped.GameSeconds);
        Assert.Equal("BountyMission",    roundtripped.MissionSubclass);
        Assert.Equal("sys-zoran",        roundtripped.SourceSystemId);
        Assert.Equal("BountyGuild",      roundtripped.SourceFaction);
        Assert.Equal(Outcome.Completed,  roundtripped.Outcome);
        Assert.Equal(5000L,              roundtripped.RewardsCredits);
    }

    [Fact]
    public void Serialization_OmitsNullEventFields()
    {
        // NullValueHandling.Ignore keeps the sidecar compact: a minimal
        // Accepted event should NOT emit the terminal-only fields.
        var evt = TestEvents.Baseline(eventId: "minimal");
        var schema = new LogSchema(LogSchema.CurrentVersion, new[] { evt });

        var json = JsonConvert.SerializeObject(schema, LogSchema.SerializerSettings);

        Assert.DoesNotContain("\"outcome\":",             json);
        Assert.DoesNotContain("\"rewardsCredits\":",      json);
        Assert.DoesNotContain("\"rewardsExperience\":",   json);
        Assert.DoesNotContain("\"rewardsReputation\":",   json);
    }

    [Fact]
    public void Serialization_UsesCamelCase()
    {
        var evt = TestEvents.Baseline(eventId: "e1", sourceSystemId: "sys-zoran");
        var schema = new LogSchema(LogSchema.CurrentVersion, new[] { evt });

        var json = JsonConvert.SerializeObject(schema, LogSchema.SerializerSettings);

        Assert.Contains("\"version\":",         json);
        Assert.Contains("\"events\":",          json);
        Assert.Contains("\"eventId\":",         json);
        Assert.Contains("\"sourceSystemId\":",  json);
        Assert.DoesNotContain("\"EventId\":",   json);
        Assert.DoesNotContain("\"SourceSystemId\":", json);
    }
}
