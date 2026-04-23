using System.Collections.Generic;
using VGMissionLog.Logging;

namespace VGMissionLog.Api;

/// <summary>
/// Converts the internal <see cref="ActivityEvent"/> record into the
/// neutral <c>IReadOnlyDictionary&lt;string, object?&gt;</c> shape the
/// public API exposes. Keys mirror the JSON sidecar's camelCase schema.
///
/// <para>Reputation rewards are emitted as
/// <c>IReadOnlyList&lt;IReadOnlyDictionary&lt;string, object?&gt;&gt;</c>
/// so consumers don't need the internal record type.</para>
/// </summary>
internal static class ActivityEventMapper
{
    public static IReadOnlyDictionary<string, object?> ToDict(ActivityEvent evt)
    {
        // Always-present keys: identity, lifecycle type, timestamps, raw
        // game-provided subclass/level. Nullable keys below are omitted when
        // null — consistent with the JSON sidecar's NullValueHandling.Ignore,
        // so wire-level and API-level shapes match.
        var d = new Dictionary<string, object?>(capacity: 24)
        {
            ["eventId"]         = evt.EventId,
            ["type"]            = evt.Type.ToString(),
            ["gameSeconds"]     = evt.GameSeconds,
            ["realUtc"]         = evt.RealUtc,
            ["storyId"]         = evt.StoryId,
            ["missionSubclass"] = evt.MissionSubclass,
            ["missionLevel"]    = evt.MissionLevel,
            ["playerLevel"]     = evt.PlayerLevel,
        };

        if (evt.MissionName != null) d["missionName"] = evt.MissionName;
        if (evt.Outcome     != null) d["outcome"]     = evt.Outcome.Value.ToString();

        // Source
        if (evt.SourceStationId   != null) d["sourceStationId"]    = evt.SourceStationId;
        if (evt.SourceStationName != null) d["sourceStationName"]  = evt.SourceStationName;
        if (evt.SourceSystemId    != null) d["sourceSystemId"]     = evt.SourceSystemId;
        if (evt.SourceSystemName  != null) d["sourceSystemName"]   = evt.SourceSystemName;
        if (evt.SourceSectorId    != null) d["sourceSectorId"]     = evt.SourceSectorId;
        if (evt.SourceSectorName  != null) d["sourceSectorName"]   = evt.SourceSectorName;
        if (evt.SourceFaction     != null) d["sourceFaction"]      = evt.SourceFaction;

        // Target
        if (evt.TargetStationId   != null) d["targetStationId"]    = evt.TargetStationId;
        if (evt.TargetStationName != null) d["targetStationName"]  = evt.TargetStationName;
        if (evt.TargetSystemId    != null) d["targetSystemId"]     = evt.TargetSystemId;

        // Rewards
        if (evt.RewardsCredits    != null) d["rewardsCredits"]     = evt.RewardsCredits;
        if (evt.RewardsExperience != null) d["rewardsExperience"]  = evt.RewardsExperience;
        if (evt.RewardsReputation != null)
        {
            var reps = new List<IReadOnlyDictionary<string, object?>>(evt.RewardsReputation.Count);
            foreach (var r in evt.RewardsReputation)
            {
                reps.Add(new Dictionary<string, object?>
                {
                    ["faction"] = r.Faction,
                    ["amount"]  = r.Amount,
                });
            }
            d["rewardsReputation"] = reps;
        }

        // Player snapshot
        if (evt.PlayerShipName        != null) d["playerShipName"]        = evt.PlayerShipName;
        if (evt.PlayerShipLevel       != null) d["playerShipLevel"]       = evt.PlayerShipLevel;
        if (evt.PlayerCurrentSystemId != null) d["playerCurrentSystemId"] = evt.PlayerCurrentSystemId;

        return d;
    }

    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> ToDicts(
        IReadOnlyList<ActivityEvent> events)
    {
        var result = new List<IReadOnlyDictionary<string, object?>>(events.Count);
        foreach (var evt in events) result.Add(ToDict(evt));
        return result;
    }
}
