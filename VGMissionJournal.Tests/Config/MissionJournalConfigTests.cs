using System;
using System.IO;
using BepInEx.Configuration;
using VGMissionJournal.Config;
using Xunit;

namespace VGMissionJournal.Tests.Config;

public class MissionJournalConfigTests : IDisposable
{
    private readonly string _tmpPath;

    public MissionJournalConfigTests()
    {
        _tmpPath = Path.Combine(Path.GetTempPath(), $"vgmissionjournal-cfg-{Guid.NewGuid():N}.cfg");
    }

    public void Dispose()
    {
        if (File.Exists(_tmpPath)) { try { File.Delete(_tmpPath); } catch { } }
    }

    private MissionJournalConfig Build() =>
        new(new ConfigFile(_tmpPath, saveOnInit: false));

    [Fact]
    public void Verbose_DefaultsToFalse()
    {
        Assert.False(Build().Verbose.Value);
    }

    [Fact]
    public void MaxMissions_DefaultsTo2000()
    {
        Assert.Equal(2000, Build().MaxMissions.Value);
    }

    [Fact]
    public void ConfigEntries_LiveInExpectedSections()
    {
        var cfg = Build();

        Assert.Equal("Journal",      cfg.Verbose.Definition.Section);
        Assert.Equal("Verbose",      cfg.Verbose.Definition.Key);
        Assert.Equal("Journal",      cfg.MaxMissions.Definition.Section);
        Assert.Equal("MaxMissions",  cfg.MaxMissions.Definition.Key);
    }

    [Fact]
    public void Settings_PersistAcrossReloads()
    {
        // Write a value, rebuild the config from the same file — setting survives.
        var first = Build();
        first.Verbose.Value      = true;
        first.MaxMissions.Value  = 500;
        // Touch the file by invoking Save via BepInEx's ConfigFile.
        first.Verbose.ConfigFile.Save();

        var second = Build();
        Assert.True(second.Verbose.Value);
        Assert.Equal(500, second.MaxMissions.Value);
    }
}
