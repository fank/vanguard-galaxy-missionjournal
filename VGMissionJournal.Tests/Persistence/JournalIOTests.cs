using System;
using System.IO;
using System.Linq;
using VGMissionJournal.Logging;
using VGMissionJournal.Persistence;
using VGMissionJournal.Tests.Support;
using Xunit;

namespace VGMissionJournal.Tests.Persistence;

// Uses a disposable tmp dir per test to keep filesystem isolation tight
// across xUnit parallelism.
public class LogIOTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly JournalIO  _io;
    private readonly DateTime _quarantineStamp = new(2026, 4, 23, 23, 0, 0, DateTimeKind.Utc);

    public LogIOTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "vgmissionjournal-io-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
        _io = new JournalIO(() => _quarantineStamp);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tmpDir))
        {
            try { Directory.Delete(_tmpDir, recursive: true); }
            catch { /* best-effort tmp cleanup */ }
        }
    }

    private string FilePath(string name) => Path.Combine(_tmpDir, name);

    // --- Read -----------------------------------------------------------

    [Fact]
    public void Read_MissingFile_ReturnsMissingFile()
    {
        var result = _io.Read(FilePath("never-created.save.vgmissionjournal.json"));

        Assert.Equal(JournalReadStatus.MissingFile, result.Status);
        Assert.Null(result.Schema);
        Assert.Null(result.QuarantinedTo);
    }

    [Fact]
    public void Read_CorruptJson_QuarantinesAndReturnsCorrupted()
    {
        var path = FilePath("MySave.save.vgmissionjournal.json");
        File.WriteAllText(path, "{this is not valid JSON");

        var result = _io.Read(path);

        Assert.Equal(JournalReadStatus.Corrupted, result.Status);
        Assert.Null(result.Schema);
        Assert.NotNull(result.QuarantinedTo);
        Assert.True(File.Exists(result.QuarantinedTo!),
            "Quarantined file should exist on disk.");
        Assert.False(File.Exists(path),
            "Original corrupt file should be moved out of the way.");
    }

    [Fact]
    public void Read_UnsupportedVersion_Quarantines()
    {
        var path = FilePath("future.save.vgmissionjournal.json");
        File.WriteAllText(path, "{\"version\":999,\"missions\":[]}");

        var result = _io.Read(path);

        Assert.Equal(JournalReadStatus.UnsupportedVersion, result.Status);
        Assert.NotNull(result.QuarantinedTo);
        Assert.True(File.Exists(result.QuarantinedTo!));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Read_ValidCurrentVersion_Loads()
    {
        var path = FilePath("valid.save.vgmissionjournal.json");
        var schema = new JournalSchema(JournalSchema.CurrentVersion,
            new[] { TestRecords.Record(instanceId: "abc") });
        _io.Write(path, schema);

        var result = _io.Read(path);

        Assert.Equal(JournalReadStatus.Loaded, result.Status);
        Assert.NotNull(result.Schema);
        Assert.Equal(JournalSchema.CurrentVersion, result.Schema!.Version);
        Assert.Single(result.Schema.Missions);
        Assert.Equal("abc", result.Schema.Missions[0].MissionInstanceId);
        Assert.Null(result.QuarantinedTo);
    }

    [Fact]
    public void Read_V1Payload_MigratesToV3()
    {
        // Minimal v1 sidecar with one Accepted + one Completed event for
        // the same missionInstanceId. The migrator should fold these into
        // a single v3 MissionRecord with a two-entry timeline.
        var path = FilePath("legacy.save.vgmissionjournal.json");
        const string v1Payload =
            "{\"version\":1,\"events\":[" +
            "{" +
              "\"eventId\":\"e1\"," +
              "\"type\":\"Accepted\"," +
              "\"gameSeconds\":10.0," +
              "\"realUtc\":\"2026-01-01T00:00:00.0000000Z\"," +
              "\"storyId\":\"story-x\"," +
              "\"missionInstanceId\":\"inst-x\"," +
              "\"missionSubclass\":\"BountyMission\"," +
              "\"missionLevel\":1," +
              "\"sourceSystemId\":\"sys-zoran\"," +
              "\"sourceFaction\":\"BountyGuild\"," +
              "\"playerLevel\":5" +
            "}," +
            "{" +
              "\"eventId\":\"e2\"," +
              "\"type\":\"Completed\"," +
              "\"gameSeconds\":20.0," +
              "\"realUtc\":\"2026-01-01T00:00:10.0000000Z\"," +
              "\"storyId\":\"story-x\"," +
              "\"missionInstanceId\":\"inst-x\"," +
              "\"missionSubclass\":\"BountyMission\"," +
              "\"missionLevel\":1," +
              "\"rewardsCredits\":1500," +
              "\"playerLevel\":5" +
            "}]}";
        File.WriteAllText(path, v1Payload);

        var result = _io.Read(path);

        Assert.Equal(JournalReadStatus.Loaded, result.Status);
        Assert.NotNull(result.Schema);
        Assert.Equal(JournalSchema.CurrentVersion, result.Schema!.Version);
        Assert.Single(result.Schema.Missions);
        var r = result.Schema.Missions[0];
        Assert.Equal("inst-x",       r.MissionInstanceId);
        Assert.Equal("story-x",      r.StoryId);
        Assert.Equal("BountyMission", r.MissionSubclass);
        Assert.Equal("sys-zoran",    r.SourceSystemId);
        Assert.Equal(2,              r.Timeline.Count);
        Assert.Equal(TimelineState.Accepted,  r.Timeline[0].State);
        Assert.Equal(10.0,                    r.Timeline[0].GameSeconds);
        Assert.Equal(TimelineState.Completed, r.Timeline[1].State);
        Assert.Equal(20.0,                    r.Timeline[1].GameSeconds);
        Assert.Equal(Outcome.Completed,       r.Outcome);
        // Typed v1 rewards fold into the unified rewards list.
        Assert.Contains(r.Rewards, rw => rw.Type == "Credits");
    }

    // --- Write ----------------------------------------------------------

    [Fact]
    public void Write_CreatesFile_AndNoTempLeftBehind()
    {
        var path = FilePath("clean.save.vgmissionjournal.json");
        var schema = new JournalSchema(JournalSchema.CurrentVersion, Array.Empty<MissionRecord>());

        _io.Write(path, schema);

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"),
            ".tmp intermediate must be cleaned up on successful write.");
    }

    [Fact]
    public void Write_OverwritesExistingFile()
    {
        var path = FilePath("overwrite.save.vgmissionjournal.json");
        _io.Write(path, new JournalSchema(JournalSchema.CurrentVersion,
            new[] { TestRecords.Record(instanceId: "first") }));

        _io.Write(path, new JournalSchema(JournalSchema.CurrentVersion,
            new[] { TestRecords.Record(instanceId: "second") }));

        var result = _io.Read(path);
        Assert.Equal(JournalReadStatus.Loaded, result.Status);
        Assert.Equal("second", result.Schema!.Missions[0].MissionInstanceId);
    }

    [Fact]
    public void Write_ThenRead_RoundTripsMultipleRecords()
    {
        var path = FilePath("roundtrip.save.vgmissionjournal.json");
        var records = new[]
        {
            TestRecords.Record(instanceId: "a", storyId: "m1", acceptedAt: 1.0,
                terminal: TimelineState.Completed, terminalAt: 2.0,
                subclass: "BountyMission",
                sourceSystemId: "sys-zoran", sourceFaction: "BountyGuild"),
            TestRecords.Record(instanceId: "b", storyId: "m1", acceptedAt: 3.0,
                subclass: "BountyMission",
                sourceSystemId: "sys-zoran", sourceFaction: "BountyGuild"),
            TestRecords.Record(instanceId: "c", storyId: "story-custom-x", acceptedAt: 4.0,
                subclass: "Mission",
                sourceSystemId: "sys-helion"),
        };

        _io.Write(path, new JournalSchema(JournalSchema.CurrentVersion, records));
        var result = _io.Read(path);

        Assert.Equal(JournalReadStatus.Loaded, result.Status);
        Assert.Equal(3, result.Schema!.Missions.Length);
        Assert.Equal(new[] { "a", "b", "c" },
                     result.Schema.Missions.Select(m => m.MissionInstanceId).ToArray());
        Assert.Equal("Mission", result.Schema.Missions[2].MissionSubclass);
        Assert.Equal(Outcome.Completed, result.Schema.Missions[0].Outcome);
    }

    [Fact]
    public void Write_NullArgs_Throw()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _io.Write(null!, new JournalSchema(JournalSchema.CurrentVersion, Array.Empty<MissionRecord>())));
        Assert.Throws<ArgumentNullException>(() =>
            _io.Write(FilePath("x.json"), null!));
    }

    // --- Quarantine naming ---------------------------------------------

    [Fact]
    public void Quarantine_UsesInjectedUtcTimestamp()
    {
        var path = FilePath("to-be-corrupt.save.vgmissionjournal.json");
        File.WriteAllText(path, "!!not-json!!");

        var result = _io.Read(path);

        Assert.EndsWith(".vgmissionjournal.corrupt.20260423230000.json", result.QuarantinedTo);
    }
}
