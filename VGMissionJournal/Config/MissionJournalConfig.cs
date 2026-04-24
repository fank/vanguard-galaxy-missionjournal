using BepInEx.Configuration;
using VGMissionJournal.Logging;

namespace VGMissionJournal.Config;

/// <summary>
/// BepInEx config bindings (spec R4.5). Minimal surface at MVP —
/// everything else ships with hardcoded defaults; facility-specific
/// opt-outs arrive post-MVP if needed.
/// </summary>
internal sealed class MissionJournalConfig
{
    public ConfigEntry<bool> Verbose          { get; }
    public ConfigEntry<int>  MaxMissions      { get; }

    public MissionJournalConfig(ConfigFile file)
    {
        Verbose = file.Bind(
            section:  "Journal",
            key:      "Verbose",
            defaultValue: false,
            description: "When true, emit a Debug-level log line for every captured " +
                         "mission lifecycle event. Off by default — only the summary " +
                         "lines (load/flush/eviction) appear at Info.");

        MaxMissions = file.Bind(
            section:  "Journal",
            key:      "MaxMissions",
            defaultValue: MissionStore.DefaultMaxMissions,
            description: "Maximum mission records kept in the log. 0 = unbounded.");
    }
}
