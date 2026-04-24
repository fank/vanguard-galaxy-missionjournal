using System.Collections.Generic;
using VGMissionLog.Logging;

namespace VGMissionLog.Api;

internal static class MissionRecordMapper
{
    public static IReadOnlyDictionary<string, object?> ToDict(MissionRecord r) => new Dictionary<string, object?>
    {
        ["storyId"]               = r.StoryId,
        ["missionInstanceId"]     = r.MissionInstanceId,
        ["missionName"]           = r.MissionName,
        ["missionSubclass"]       = r.MissionSubclass,
        ["missionLevel"]          = r.MissionLevel,
        ["sourceStationId"]       = r.SourceStationId,
        ["sourceStationName"]     = r.SourceStationName,
        ["sourceSystemId"]        = r.SourceSystemId,
        ["sourceSystemName"]      = r.SourceSystemName,
        ["sourceSectorId"]        = r.SourceSectorId,
        ["sourceSectorName"]      = r.SourceSectorName,
        ["sourceFaction"]         = r.SourceFaction,
        ["targetStationId"]       = r.TargetStationId,
        ["targetStationName"]     = r.TargetStationName,
        ["targetSystemId"]        = r.TargetSystemId,
        ["playerLevel"]           = r.PlayerLevel,
        ["playerShipName"]        = r.PlayerShipName,
        ["playerShipLevel"]       = r.PlayerShipLevel,
        ["playerCurrentSystemId"] = r.PlayerCurrentSystemId,
        ["steps"]                 = MapSteps(r.Steps),
        ["rewards"]               = MapRewards(r.Rewards),
        ["timeline"]              = MapTimeline(r.Timeline),
        ["acceptedAtGameSeconds"] = (object?)r.AcceptedAtGameSeconds,
        ["terminalAtGameSeconds"] = r.TerminalAtGameSeconds,
        ["outcome"]               = r.Outcome?.ToString(),
        ["isActive"]              = r.IsActive,
    };

    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> ToDicts(
        IReadOnlyList<MissionRecord> records)
    {
        var out_ = new List<IReadOnlyDictionary<string, object?>>(records.Count);
        foreach (var r in records) out_.Add(ToDict(r));
        return out_;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> MapSteps(
        IReadOnlyList<MissionStepDefinition> steps)
    {
        var out_ = new List<IReadOnlyDictionary<string, object?>>(steps.Count);
        foreach (var s in steps)
        {
            var objs = new List<IReadOnlyDictionary<string, object?>>(s.Objectives.Count);
            foreach (var o in s.Objectives)
                objs.Add(new Dictionary<string, object?>
                {
                    ["type"]   = o.Type,
                    ["fields"] = o.Fields,
                });
            out_.Add(new Dictionary<string, object?>
            {
                ["description"]          = s.Description,
                ["requireAllObjectives"] = s.RequireAllObjectives,
                ["hidden"]               = s.Hidden,
                ["objectives"]           = objs,
            });
        }
        return out_;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> MapRewards(
        IReadOnlyList<MissionRewardSnapshot> rewards)
    {
        var out_ = new List<IReadOnlyDictionary<string, object?>>(rewards.Count);
        foreach (var r in rewards)
            out_.Add(new Dictionary<string, object?>
            {
                ["type"]   = r.Type,
                ["fields"] = r.Fields,
            });
        return out_;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> MapTimeline(
        IReadOnlyList<TimelineEntry> tl)
    {
        var out_ = new List<IReadOnlyDictionary<string, object?>>(tl.Count);
        foreach (var e in tl)
            out_.Add(new Dictionary<string, object?>
            {
                ["state"]       = e.State.ToString(),
                ["gameSeconds"] = e.GameSeconds,
                ["realUtc"]     = e.RealUtc,
            });
        return out_;
    }
}
