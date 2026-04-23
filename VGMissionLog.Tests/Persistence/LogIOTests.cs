using System;
using System.IO;
using VGMissionLog.Logging;
using VGMissionLog.Persistence;
using VGMissionLog.Tests.Support;
using Xunit;

namespace VGMissionLog.Tests.Persistence;

// Uses a disposable tmp dir per test to keep filesystem isolation tight
// across xUnit parallelism.
public class LogIOTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly LogIO  _io;
    private readonly DateTime _quarantineStamp = new(2026, 4, 23, 23, 0, 0, DateTimeKind.Utc);

    public LogIOTests()
    {
        _tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vgmissionlog-io-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
        _io = new LogIO(() => _quarantineStamp);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tmpDir))
        {
            try { Directory.Delete(_tmpDir, recursive: true); }
            catch { /* best-effort tmp cleanup */ }
        }
    }

    private string FilePath(string name) => System.IO.Path.Combine(_tmpDir, name);

    // --- Read -----------------------------------------------------------

    [Fact]
    public void Read_MissingFile_ReturnsMissingFile()
    {
        var result = _io.Read(FilePath("never-created.save.vgmissionlog.json"));

        Assert.Equal(LogReadStatus.MissingFile, result.Status);
        Assert.Null(result.Schema);
        Assert.Null(result.QuarantinedTo);
    }

    [Fact]
    public void Read_CorruptJson_QuarantinesAndReturnsCorrupted()
    {
        var path = FilePath("MySave.save.vgmissionlog.json");
        File.WriteAllText(path, "{this is not valid JSON");

        var result = _io.Read(path);

        Assert.Equal(LogReadStatus.Corrupted, result.Status);
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
        var path = FilePath("future.save.vgmissionlog.json");
        File.WriteAllText(path, "{\"version\":999,\"events\":[]}");

        var result = _io.Read(path);

        Assert.Equal(LogReadStatus.UnsupportedVersion, result.Status);
        Assert.NotNull(result.QuarantinedTo);
        Assert.True(File.Exists(result.QuarantinedTo!));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Read_ValidCurrentVersion_Loads()
    {
        var path = FilePath("valid.save.vgmissionlog.json");
        var schema = new LogSchema(LogSchema.CurrentVersion, new[] { TestEvents.Baseline(eventId: "abc") });
        _io.Write(path, schema);

        var result = _io.Read(path);

        Assert.Equal(LogReadStatus.Loaded, result.Status);
        Assert.NotNull(result.Schema);
        Assert.Equal(LogSchema.CurrentVersion, result.Schema!.Version);
        Assert.Single(result.Schema.Events);
        Assert.Equal("abc", result.Schema.Events[0].EventId);
        Assert.Null(result.QuarantinedTo);
    }

    // --- Write ----------------------------------------------------------

    [Fact]
    public void Write_CreatesFile_AndNoTempLeftBehind()
    {
        var path = FilePath("clean.save.vgmissionlog.json");
        var schema = new LogSchema(LogSchema.CurrentVersion, Array.Empty<ActivityEvent>());

        _io.Write(path, schema);

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"),
            ".tmp intermediate must be cleaned up on successful write.");
    }

    [Fact]
    public void Write_OverwritesExistingFile()
    {
        var path = FilePath("overwrite.save.vgmissionlog.json");
        _io.Write(path, new LogSchema(LogSchema.CurrentVersion, new[] { TestEvents.Baseline(eventId: "first") }));

        _io.Write(path, new LogSchema(LogSchema.CurrentVersion, new[] { TestEvents.Baseline(eventId: "second") }));

        var result = _io.Read(path);
        Assert.Equal(LogReadStatus.Loaded, result.Status);
        Assert.Equal("second", result.Schema!.Events[0].EventId);
    }

    [Fact]
    public void Write_ThenRead_RoundTripsMultipleEvents()
    {
        var path = FilePath("roundtrip.save.vgmissionlog.json");
        var events = new[]
        {
            TestEvents.Baseline(eventId: "a", storyId: "m1", gameSeconds: 1.0,
                type: ActivityEventType.Accepted, missionType: MissionType.Bounty,
                sourceSystemId: "sys-zoran", sourceFaction: "BountyGuild"),
            TestEvents.Baseline(eventId: "b", storyId: "m1", gameSeconds: 2.0,
                type: ActivityEventType.Completed, missionType: MissionType.Bounty,
                sourceSystemId: "sys-zoran", sourceFaction: "BountyGuild")
                with { Outcome = Outcome.Completed, RewardsCredits = 1500, RewardsExperience = 200 },
            TestEvents.Baseline(eventId: "c", storyId: "vganima_llm_x", gameSeconds: 3.0,
                type: ActivityEventType.Accepted,
                missionType: MissionType.ThirdParty("vganima"),
                sourceSystemId: "sys-helion"),
        };

        _io.Write(path, new LogSchema(LogSchema.CurrentVersion, events));
        var result = _io.Read(path);

        Assert.Equal(LogReadStatus.Loaded, result.Status);
        Assert.Equal(3, result.Schema!.Events.Length);
        Assert.Equal(new[] { "a", "b", "c" },
                     Array.ConvertAll(result.Schema.Events, e => e.EventId));
        Assert.Equal(MissionType.ThirdParty("vganima"), result.Schema.Events[2].MissionType);
        Assert.Equal(1500L, result.Schema.Events[1].RewardsCredits);
    }

    [Fact]
    public void Write_NullArgs_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => _io.Write(null!, new LogSchema(1, Array.Empty<ActivityEvent>())));
        Assert.Throws<ArgumentNullException>(() => _io.Write(FilePath("x.json"), null!));
    }

    // --- Quarantine naming ---------------------------------------------

    [Fact]
    public void Quarantine_UsesInjectedUtcTimestamp()
    {
        var path = FilePath("to-be-corrupt.save.vgmissionlog.json");
        File.WriteAllText(path, "!!not-json!!");

        var result = _io.Read(path);

        Assert.EndsWith(".vgmissionlog.corrupt.20260423230000.json", result.QuarantinedTo);
    }
}
