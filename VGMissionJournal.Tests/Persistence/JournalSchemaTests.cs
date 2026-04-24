using System.Linq;
using Newtonsoft.Json;
using VGMissionJournal.Logging;
using VGMissionJournal.Persistence;
using VGMissionJournal.Tests.Support;
using Xunit;

namespace VGMissionJournal.Tests.Persistence;

public class LogSchemaTests
{
    [Fact]
    public void CurrentVersion_IsThree()
    {
        Assert.Equal(3, JournalSchema.CurrentVersion);
    }

    [Fact]
    public void EmptySchema_RoundTripsThroughNewtonsoft()
    {
        var schema = new JournalSchema(JournalSchema.CurrentVersion, System.Array.Empty<MissionRecord>());

        var json   = JsonConvert.SerializeObject(schema, JournalSchema.SerializerSettings);
        var back   = JsonConvert.DeserializeObject<JournalSchema>(json, JournalSchema.SerializerSettings);

        Assert.NotNull(back);
        Assert.Equal(JournalSchema.CurrentVersion, back!.Version);
        Assert.Empty(back.Missions);
    }

    [Fact]
    public void SingleMission_RoundTripsPrimitiveFields()
    {
        var original = TestRecords.Record(
            instanceId: "abc",
            storyId: "m1",
            acceptedAt: 123.45,
            terminal: TimelineState.Completed,
            terminalAt: 200.0,
            subclass: "BountyMission",
            sourceSystemId: "sys-zoran",
            sourceFaction:  "BountyGuild");

        var schema = new JournalSchema(JournalSchema.CurrentVersion, new[] { original });

        var json = JsonConvert.SerializeObject(schema, JournalSchema.SerializerSettings);
        var back = JsonConvert.DeserializeObject<JournalSchema>(json, JournalSchema.SerializerSettings)!;

        Assert.Single(back.Missions);
        var r = back.Missions[0];
        Assert.Equal("abc",              r.MissionInstanceId);
        Assert.Equal("m1",               r.StoryId);
        Assert.Equal(123.45,             r.AcceptedAtGameSeconds);
        Assert.Equal("BountyMission",    r.MissionSubclass);
        Assert.Equal("sys-zoran",        r.SourceSystemId);
        Assert.Equal("BountyGuild",      r.SourceFaction);
        Assert.Equal(Outcome.Completed,  r.Outcome);
        Assert.Equal(200.0,              r.TerminalAtGameSeconds);
    }

    [Fact]
    public void Serialization_OmitsNullMissionFields()
    {
        // NullValueHandling.Ignore keeps the sidecar compact: an active
        // record with no target/sector data should NOT emit those fields.
        var r = TestRecords.Record(instanceId: "minimal");
        var schema = new JournalSchema(JournalSchema.CurrentVersion, new[] { r });

        var json = JsonConvert.SerializeObject(schema, JournalSchema.SerializerSettings);

        Assert.DoesNotContain("\"targetStationId\":",   json);
        Assert.DoesNotContain("\"targetSystemId\":",    json);
        Assert.DoesNotContain("\"sourceSectorId\":",    json);
    }

    [Fact]
    public void Serialization_EmitsEnumsAsStrings()
    {
        // Timeline states serialize as strings, not integers.
        var r = TestRecords.Record(instanceId: "e1",
            terminal: TimelineState.Completed, terminalAt: 50.0);
        var schema = new JournalSchema(JournalSchema.CurrentVersion, new[] { r });

        var json = JsonConvert.SerializeObject(schema, JournalSchema.SerializerSettings);

        Assert.Contains("\"state\": \"Accepted\"",  json);
        Assert.Contains("\"state\": \"Completed\"", json);
        // No raw integer enum emissions.
        Assert.DoesNotContain("\"state\": 0", json);
        Assert.DoesNotContain("\"state\": 1", json);
        Assert.DoesNotContain("\"state\": 2", json);
    }

    [Fact]
    public void Serialization_UsesCamelCase()
    {
        var r = TestRecords.Record(instanceId: "e1", sourceSystemId: "sys-zoran");
        var schema = new JournalSchema(JournalSchema.CurrentVersion, new[] { r });

        var json = JsonConvert.SerializeObject(schema, JournalSchema.SerializerSettings);

        Assert.Contains("\"version\":",           json);
        Assert.Contains("\"missions\":",          json);
        Assert.Contains("\"missionInstanceId\":", json);
        Assert.Contains("\"sourceSystemId\":",    json);
        Assert.DoesNotContain("\"MissionInstanceId\":", json);
        Assert.DoesNotContain("\"SourceSystemId\":",    json);
    }

    [Fact]
    public void Serialization_TopLevelFieldsUseLowercase()
    {
        // JournalSchema's positional record parameters carry [JsonProperty]
        // attributes pinning `version` and `missions` to lowercase, so the
        // sidecar shape is stable even if the camelCase resolver ever changes.
        var schema = new JournalSchema(JournalSchema.CurrentVersion, System.Array.Empty<MissionRecord>());

        var json = JsonConvert.SerializeObject(schema, JournalSchema.SerializerSettings);

        Assert.Contains("\"version\":",  json);
        Assert.Contains("\"missions\":", json);
    }
}
