using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VGMissionJournal.Logging;
using VGMissionJournal.Persistence;
using Xunit;

namespace VGMissionJournal.Tests.Persistence;

/// <summary>
/// Pins the on-disk JSON shape against drift from C# computed-property
/// auto-serialization. Every public <c>get</c> property on a record becomes
/// a JSON key by default; these tests make sure the <see cref="JsonIgnore"/>
/// annotations on derived helpers actually hold, independent of whoever
/// touched the record last.
/// </summary>
public class SchemaWireFormatTests
{
    private static MissionRecord SampleActive() => new(
        StoryId:               "",
        MissionInstanceId:     "inst-1",
        MissionName:           "Test",
        MissionSubclass:       "Mission",
        MissionLevel:          1,
        SourceStationId:       null, SourceStationName: null,
        SourceSystemId:        "sys-a", SourceSystemName: "A",
        SourceSectorId:        null, SourceSectorName: null,
        SourceFaction:         null,
        TargetStationId:       null, TargetStationName: null, TargetSystemId: null,
        PlayerLevel:           1, PlayerShipName: null, PlayerShipLevel: null,
        PlayerCurrentSystemId: null,
        Steps:                 Array.Empty<MissionStepDefinition>(),
        Rewards:               Array.Empty<MissionRewardSnapshot>(),
        Timeline:              new[] { new TimelineEntry(TimelineState.Accepted, 10.0, "2026-01-01T00:00:00Z") });

    private static MissionRecord SampleCompleted() => SampleActive() with
    {
        Timeline = new[]
        {
            new TimelineEntry(TimelineState.Accepted,  10.0, "2026-01-01T00:00:00Z"),
            new TimelineEntry(TimelineState.Completed, 42.0, "2026-01-01T00:00:32Z"),
        },
    };

    private static JObject Serialize(MissionRecord r)
    {
        var schema = new JournalSchema(JournalSchema.CurrentVersion, new[] { r });
        var json   = JsonConvert.SerializeObject(schema, JournalSchema.SerializerSettings);
        var root   = JObject.Parse(json);
        return (JObject)root["missions"]![0]!;
    }

    [Fact]
    public void ActiveMission_WireJson_OmitsComputedHelpers()
    {
        var json = Serialize(SampleActive());

        Assert.Null(json["isActive"]);
        Assert.Null(json["outcome"]);
        Assert.Null(json["acceptedAtGameSeconds"]);
        Assert.Null(json["terminalAtGameSeconds"]);
    }

    [Fact]
    public void CompletedMission_WireJson_OmitsComputedHelpers()
    {
        // Exercising the terminal path — TerminalEntry resolves non-null,
        // so the computed properties return concrete values. They still
        // must not leak to the wire.
        var json = Serialize(SampleCompleted());

        Assert.Null(json["isActive"]);
        Assert.Null(json["outcome"]);
        Assert.Null(json["acceptedAtGameSeconds"]);
        Assert.Null(json["terminalAtGameSeconds"]);
    }

    [Fact]
    public void TimelineEntry_WireJson_OmitsIsTerminal()
    {
        var json = Serialize(SampleCompleted());
        var timeline = (JArray)json["timeline"]!;

        Assert.Equal(2, timeline.Count);
        foreach (var entry in timeline)
            Assert.Null(entry["isTerminal"]);
    }

    [Fact]
    public void RoundTrip_OmittedKeys_DoNotPreventDeserialization()
    {
        // Derived helpers can still be read by the C# consumer after a
        // round-trip — they're recomputed from the timeline on each access.
        var original   = SampleCompleted();
        var serialized = JsonConvert.SerializeObject(
            new JournalSchema(JournalSchema.CurrentVersion, new[] { original }),
            JournalSchema.SerializerSettings);
        var reloaded   = JsonConvert.DeserializeObject<JournalSchema>(
            serialized, JournalSchema.SerializerSettings)!;

        var m = reloaded.Missions[0];
        Assert.False(m.IsActive);
        Assert.Equal(Outcome.Completed, m.Outcome);
        Assert.Equal(10.0, m.AcceptedAtGameSeconds);
        Assert.Equal(42.0, m.TerminalAtGameSeconds);
        Assert.True(m.Timeline[1].IsTerminal);
    }

    [Fact]
    public void LegacyKeysOnRead_AreIgnored_DoNotBreakDeserialize()
    {
        // v0.1.1 wrote these keys explicitly. Readers built against v0.1.2+
        // must tolerate them — NullValueHandling.Ignore + missing-member
        // tolerance in Newtonsoft's default behaviour handles it, but
        // pinning it here prevents someone from flipping
        // MissingMemberHandling to Error without noticing.
        const string legacyPayload = @"
        {
          ""version"": 3,
          ""missions"": [
            {
              ""storyId"": """",
              ""missionInstanceId"": ""inst-legacy"",
              ""missionName"": ""Legacy"",
              ""missionSubclass"": ""Mission"",
              ""missionLevel"": 0,
              ""playerLevel"": 0,
              ""steps"": [],
              ""rewards"": [],
              ""timeline"": [
                { ""state"": ""Accepted"", ""gameSeconds"": 10.0, ""realUtc"": ""2026-01-01T00:00:00Z"", ""isTerminal"": false }
              ],
              ""isActive"": true,
              ""acceptedAtGameSeconds"": 10.0
            }
          ]
        }";

        var schema = JsonConvert.DeserializeObject<JournalSchema>(
            legacyPayload, JournalSchema.SerializerSettings);

        Assert.NotNull(schema);
        var m = schema!.Missions[0];
        Assert.Equal("inst-legacy", m.MissionInstanceId);
        Assert.True(m.IsActive);
        Assert.Equal(10.0, m.AcceptedAtGameSeconds);
    }
}
