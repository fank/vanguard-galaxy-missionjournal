using BepInEx.Configuration;
using VGMissionLog.Logging;

namespace VGMissionLog.Config;

/// <summary>
/// BepInEx config bindings (spec R4.5). Minimal surface at MVP —
/// everything else ships with hardcoded defaults; facility-specific
/// opt-outs arrive post-MVP if needed.
/// </summary>
internal sealed class MissionLogConfig
{
    public ConfigEntry<bool> Verbose          { get; }
    public ConfigEntry<int>  MaxEvents        { get; }

    public MissionLogConfig(ConfigFile file)
    {
        Verbose = file.Bind(
            section:  "Logging",
            key:      "Verbose",
            defaultValue: false,
            description: "When true, emit a Debug-level log line for every captured " +
                         "mission lifecycle event. Off by default — only the summary " +
                         "lines (load/flush/eviction) appear at Info.");

        MaxEvents = file.Bind(
            section:  "Persistence",
            key:      "MaxEvents",
            defaultValue: ActivityLog.DefaultMaxEvents,
            description: "Soft cap on retained events per save (FIFO eviction). Each " +
                         "event is ~500 bytes serialised; 2000 × 500 B ≈ 1 MB per sidecar.");
    }
}
