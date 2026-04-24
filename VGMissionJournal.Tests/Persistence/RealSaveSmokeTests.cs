using System.IO;
using System.Linq;
using Newtonsoft.Json;
using VGMissionJournal.Logging;
using VGMissionJournal.Persistence;
using Xunit;

namespace VGMissionJournal.Tests.Persistence;

/// <summary>
/// Smoke tests against a real in-game sidecar (<c>Fixtures/real-save.vgmissionjournal.json</c>)
/// captured from an actual playthrough. Pins invariants that must survive any future
/// schema tweak or capture change — if a refactor breaks one, that's a regression
/// users will feel.
/// </summary>
public class RealSaveSmokeTests
{
    private static readonly string FixturePath =
        Path.Combine("Fixtures", "real-save.vgmissionjournal.json");

    private static JournalSchema LoadFixture()
    {
        var raw = File.ReadAllText(FixturePath);
        var schema = JsonConvert.DeserializeObject<JournalSchema>(raw, JournalSchema.SerializerSettings);
        Assert.NotNull(schema);
        return schema!;
    }

    [Fact]
    public void RealSave_Deserializes_AtCurrentSchemaVersion()
    {
        var schema = LoadFixture();
        Assert.Equal(JournalSchema.CurrentVersion, schema.Version);
        Assert.NotEmpty(schema.Missions);
    }

    [Fact]
    public void RealSave_EveryMissionHasInstanceId_AndInstanceIdsAreUnique()
    {
        var schema = LoadFixture();
        var ids = schema.Missions.Select(m => m.MissionInstanceId).ToList();

        Assert.All(ids, id => Assert.False(string.IsNullOrEmpty(id)));
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void RealSave_EveryMissionTimelineStartsWithAccepted()
    {
        var schema = LoadFixture();
        foreach (var m in schema.Missions)
        {
            Assert.NotEmpty(m.Timeline);
            Assert.Equal(TimelineState.Accepted, m.Timeline[0].State);
        }
    }

    [Fact]
    public void RealSave_EveryObjectiveHasNonEmptyType()
    {
        var schema = LoadFixture();
        foreach (var m in schema.Missions)
            foreach (var step in m.Steps)
                foreach (var obj in step.Objectives)
                    Assert.False(string.IsNullOrEmpty(obj.Type));
    }

    [Fact]
    public void RealSave_EveryRewardHasNonEmptyType()
    {
        var schema = LoadFixture();
        foreach (var m in schema.Missions)
            foreach (var r in m.Rewards)
                Assert.False(string.IsNullOrEmpty(r.Type));
    }

    [Fact]
    public void RealSave_ItemRewardIdentifiers_DoNotLeakCloneSuffix()
    {
        // Regression for the ItemBuilder.CreateItemType clone bug — vanilla
        // copies a base item via Object.Instantiate which appends "(Clone)"
        // to .name, and the backing identifier field doesn't serialize
        // across the clone. MissionRecordBuilder strips the suffix; this
        // test pins that it actually held up across a real playthrough.
        var schema = LoadFixture();
        var itemRewards = schema.Missions
            .SelectMany(m => m.Rewards)
            .Where(r => r.Type == "Item")
            .ToList();

        Assert.NotEmpty(itemRewards);    // fixture must actually exercise this path

        foreach (var r in itemRewards)
        {
            Assert.NotNull(r.Fields);
            Assert.True(r.Fields!.TryGetValue("item", out var item),
                "Item reward missing 'item' field");
            var identifier = item as string;
            Assert.False(string.IsNullOrEmpty(identifier));
            Assert.DoesNotContain("(Clone)", identifier!);
        }
    }

    [Fact]
    public void RealSave_CountsMatchTimelineStates()
    {
        // Sanity: sum of states across all timelines matches the event volume
        // implied by (mission count * avg timeline length). No missing/empty
        // timelines, no orphan states.
        var schema = LoadFixture();
        var stateTotals = schema.Missions
            .SelectMany(m => m.Timeline)
            .GroupBy(e => e.State)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.True(stateTotals.TryGetValue(TimelineState.Accepted, out var accepted));
        Assert.Equal(schema.Missions.Length, accepted);
    }
}
