using System;
using System.IO;
using BepInEx.Configuration;
using VGMissionLog.Config;
using Xunit;

namespace VGMissionLog.Tests.Config;

public class MissionLogConfigTests : IDisposable
{
    private readonly string _tmpPath;

    public MissionLogConfigTests()
    {
        _tmpPath = Path.Combine(Path.GetTempPath(), $"vgmissionlog-cfg-{Guid.NewGuid():N}.cfg");
    }

    public void Dispose()
    {
        if (File.Exists(_tmpPath)) { try { File.Delete(_tmpPath); } catch { } }
    }

    private MissionLogConfig Build() =>
        new(new ConfigFile(_tmpPath, saveOnInit: false));

    [Fact]
    public void Verbose_DefaultsToFalse()
    {
        Assert.False(Build().Verbose.Value);
    }

    [Fact]
    public void MaxEvents_DefaultsTo2000()
    {
        Assert.Equal(2000, Build().MaxEvents.Value);
    }

    [Fact]
    public void ConfigEntries_LiveInExpectedSections()
    {
        var cfg = Build();

        Assert.Equal("Logging",     cfg.Verbose.Definition.Section);
        Assert.Equal("Verbose",     cfg.Verbose.Definition.Key);
        Assert.Equal("Persistence", cfg.MaxEvents.Definition.Section);
        Assert.Equal("MaxEvents",   cfg.MaxEvents.Definition.Key);
    }

    [Fact]
    public void Settings_PersistAcrossReloads()
    {
        // Write a value, rebuild the config from the same file — setting survives.
        var first = Build();
        first.Verbose.Value   = true;
        first.MaxEvents.Value = 500;
        // Touch the file by invoking Save via BepInEx's ConfigFile.
        first.Verbose.ConfigFile.Save();

        var second = Build();
        Assert.True(second.Verbose.Value);
        Assert.Equal(500, second.MaxEvents.Value);
    }
}
