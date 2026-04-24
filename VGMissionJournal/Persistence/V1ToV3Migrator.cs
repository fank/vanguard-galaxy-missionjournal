using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using VGMissionJournal.Logging;

namespace VGMissionJournal.Persistence;

/// <summary>
/// Upgrades a v1 sidecar (flat <c>events[]</c>) into a v3 sidecar
/// (<c>missions[]</c> with explicit timelines). Called only by
/// <see cref="JournalIO.Read"/> when <c>schema.Version == 1</c>.
///
/// <para>Fidelity: v1 did not record per-step transitions, so migrated
/// timelines hold <see cref="TimelineState.Accepted"/> and one terminal
/// entry (if the mission terminated in the v1 log). Missions with no
/// Accepted event in v1 are dropped — we'd have to invent identity fields
/// and migration prefers honest gaps over fabricated data.</para>
///
/// <para>Typed v1 rewards (<c>rewardsCredits</c>, <c>rewardsExperience</c>,
/// <c>rewardsReputation</c>) fold into <see cref="MissionRecord.Rewards"/>
/// as <c>Credits</c> / <c>Experience</c> / <c>Reputation</c> entries. Any
/// unified v1 <c>rewards[]</c> already present is passed through as-is.</para>
/// </summary>
internal static class V1ToV3Migrator
{
    public static JournalSchema Migrate(string v1Payload)
    {
        var v1 = JsonConvert.DeserializeObject<V1Schema>(v1Payload, V1SerializerSettings)
                 ?? throw new JsonException("V1 schema was null");
        if (v1.Version != 1)
            throw new InvalidOperationException($"V1ToV3Migrator called on version {v1.Version}");

        // Group events by missionInstanceId (fallback: storyId if instance id is empty).
        var groups = new Dictionary<string, List<V1Event>>(StringComparer.Ordinal);
        foreach (var e in v1.Events ?? Array.Empty<V1Event>())
        {
            var key = !string.IsNullOrEmpty(e.MissionInstanceId) ? e.MissionInstanceId!
                    : !string.IsNullOrEmpty(e.StoryId)           ? e.StoryId!
                                                                  : $"anon:{Guid.NewGuid()}";
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<V1Event>();
                groups[key] = list;
            }
            list.Add(e);
        }

        var records = new List<MissionRecord>();
        foreach (var kvp in groups)
        {
            var instanceId = kvp.Key;
            var events     = kvp.Value;

            var accept = events.FirstOrDefault(e =>
                string.Equals(e.Type, "Accepted", StringComparison.Ordinal));
            if (accept is null) continue;  // orphan terminal — drop

            var terminal = events.FirstOrDefault(e =>
                e.Type == "Completed" || e.Type == "Failed" || e.Type == "Abandoned");

            var timeline = new List<TimelineEntry>
            {
                new TimelineEntry(TimelineState.Accepted, accept.GameSeconds, accept.RealUtc),
            };
            if (terminal is not null)
            {
                var state = terminal.Type switch
                {
                    "Completed" => TimelineState.Completed,
                    "Failed"    => TimelineState.Failed,
                    "Abandoned" => TimelineState.Abandoned,
                    _           => TimelineState.Completed,
                };
                timeline.Add(new TimelineEntry(state, terminal.GameSeconds, terminal.RealUtc));
            }

            // Rewards: prefer terminal event's rewards, else accept's.
            var rewardsSource = terminal ?? accept;
            var rewards = FoldRewards(rewardsSource);

            records.Add(new MissionRecord(
                StoryId:               accept.StoryId ?? instanceId,
                MissionInstanceId:     instanceId,
                MissionName:           accept.MissionName,
                MissionSubclass:       accept.MissionSubclass ?? "Mission",
                MissionLevel:          accept.MissionLevel,
                SourceStationId:       accept.SourceStationId,
                SourceStationName:     accept.SourceStationName,
                SourceSystemId:        accept.SourceSystemId,
                SourceSystemName:      accept.SourceSystemName,
                SourceSectorId:        accept.SourceSectorId,
                SourceSectorName:      accept.SourceSectorName,
                SourceFaction:         accept.SourceFaction,
                TargetStationId:       accept.TargetStationId,
                TargetStationName:     accept.TargetStationName,
                TargetSystemId:        accept.TargetSystemId,
                PlayerLevel:           accept.PlayerLevel,
                PlayerShipName:        accept.PlayerShipName,
                PlayerShipLevel:       accept.PlayerShipLevel,
                PlayerCurrentSystemId: accept.PlayerCurrentSystemId,
                Steps:                 Array.Empty<MissionStepDefinition>(),      // v1 rarely captured these
                Rewards:               rewards,
                Timeline:              timeline));
        }

        return new JournalSchema(Version: JournalSchema.CurrentVersion, Missions: records.ToArray());
    }

    private static IReadOnlyList<MissionRewardSnapshot> FoldRewards(V1Event e)
    {
        var list = new List<MissionRewardSnapshot>();
        if (e.Rewards is { Count: > 0 })
        {
            foreach (var r in e.Rewards) list.Add(r);
            return list;
        }
        if (e.RewardsCredits is long credits)
            list.Add(new MissionRewardSnapshot("Credits",
                new Dictionary<string, object?> { ["amount"] = credits }));
        if (e.RewardsExperience is long exp)
            list.Add(new MissionRewardSnapshot("Experience",
                new Dictionary<string, object?> { ["amount"] = exp }));
        if (e.RewardsReputation is { Count: > 0 })
        {
            foreach (var rep in e.RewardsReputation)
                list.Add(new MissionRewardSnapshot("Reputation",
                    new Dictionary<string, object?> { ["faction"] = rep.Faction, ["amount"] = rep.Amount }));
        }
        return list;
    }

    private static readonly JsonSerializerSettings V1SerializerSettings = new()
    {
        ContractResolver  = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        Converters        = { new StringEnumConverter() },
    };

    // --- Obsolete v1 shape — confined here, not exported. -----------------

    private sealed class V1Schema
    {
        public int Version { get; set; }
        public V1Event[]? Events { get; set; }
    }

    private sealed class V1Event
    {
        public string? EventId { get; set; }
        public string? Type { get; set; }
        public double GameSeconds { get; set; }
        public string? RealUtc { get; set; }
        public string? StoryId { get; set; }
        public string? MissionInstanceId { get; set; }
        public string? MissionName { get; set; }
        public string? MissionSubclass { get; set; }
        public int MissionLevel { get; set; }
        public string? SourceStationId { get; set; }
        public string? SourceStationName { get; set; }
        public string? SourceSystemId { get; set; }
        public string? SourceSystemName { get; set; }
        public string? SourceSectorId { get; set; }
        public string? SourceSectorName { get; set; }
        public string? SourceFaction { get; set; }
        public string? TargetStationId { get; set; }
        public string? TargetStationName { get; set; }
        public string? TargetSystemId { get; set; }
        public long? RewardsCredits { get; set; }
        public long? RewardsExperience { get; set; }
        public List<RepReward>? RewardsReputation { get; set; }
        public List<MissionRewardSnapshot>? Rewards { get; set; }
        public int PlayerLevel { get; set; }
        public string? PlayerShipName { get; set; }
        public int? PlayerShipLevel { get; set; }
        public string? PlayerCurrentSystemId { get; set; }
    }
}
