using System;
using System.Collections.Generic;
using System.Linq;
using VGMissionLog.Logging;
using VGMissionLog.Persistence;

namespace VGMissionLog.Api;

internal sealed class MissionLogQueryAdapter : IMissionLogQuery
{
    private readonly MissionStore _store;

    public MissionLogQueryAdapter(MissionStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public int SchemaVersion            => LogSchema.CurrentVersion;
    public int TotalMissionCount        => _store.TotalMissionCount;

    public double? OldestAcceptedGameSeconds =>
        _store.AllMissions.Count == 0 ? null : _store.AllMissions.Min(r => r.AcceptedAtGameSeconds);
    public double? NewestAcceptedGameSeconds =>
        _store.AllMissions.Count == 0 ? null : _store.AllMissions.Max(r => r.AcceptedAtGameSeconds);

    public IReadOnlyDictionary<string, object?>? GetMission(string missionInstanceId)
    {
        var r = _store.GetByInstanceId(missionInstanceId);
        return r is null ? null : MissionRecordMapper.ToDict(r);
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> GetActiveMissions() =>
        MissionRecordMapper.ToDicts(_store.GetActiveMissions());

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> GetAllMissions() =>
        MissionRecordMapper.ToDicts(_store.AllMissions);

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> GetMissionsInSystem(
        string systemId, double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue) =>
        MissionRecordMapper.ToDicts(FilterByAcceptedTime(_store.GetMissionsInSystem(systemId),
            sinceGameSeconds, untilGameSeconds));

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> GetMissionsByFaction(
        string factionId, double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue) =>
        MissionRecordMapper.ToDicts(FilterByAcceptedTime(_store.GetMissionsByFaction(factionId),
            sinceGameSeconds, untilGameSeconds));

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> GetMissionsByMissionSubclass(
        string missionSubclass, double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue)
    {
        var matched = _store.AllMissions.Where(r =>
            string.Equals(r.MissionSubclass, missionSubclass, StringComparison.Ordinal)).ToList();
        return MissionRecordMapper.ToDicts(FilterByAcceptedTime(matched, sinceGameSeconds, untilGameSeconds));
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> GetMissionsByOutcome(
        string outcome, double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue)
    {
        if (!Enum.TryParse<Outcome>(outcome, ignoreCase: false, out var parsed))
            return Array.Empty<IReadOnlyDictionary<string, object?>>();
        var matched = _store.AllMissions.Where(r => r.Outcome == parsed).ToList();
        return MissionRecordMapper.ToDicts(FilterByAcceptedTime(matched, sinceGameSeconds, untilGameSeconds));
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> GetMissionsWithObjective(
        string objectiveType, double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue)
    {
        if (string.IsNullOrEmpty(objectiveType))
            return Array.Empty<IReadOnlyDictionary<string, object?>>();
        var matched = _store.AllMissions.Where(r => RecordHasObjective(r, objectiveType)).ToList();
        return MissionRecordMapper.ToDicts(FilterByAcceptedTime(matched, sinceGameSeconds, untilGameSeconds));
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> GetMissionsForStoryId(string storyId) =>
        MissionRecordMapper.ToDicts(_store.AllMissions
            .Where(r => string.Equals(r.StoryId, storyId, StringComparison.Ordinal))
            .ToList());

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> GetRecentMissions(int count)
    {
        if (count <= 0) return Array.Empty<IReadOnlyDictionary<string, object?>>();
        var ordered = _store.AllMissions
            .OrderByDescending(r => r.AcceptedAtGameSeconds)
            .Take(count)
            .ToList();
        return MissionRecordMapper.ToDicts(ordered);
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> GetMissionsWithinJumps(
        string pivotSystemId, int maxJumps, Func<string, string, int> jumpDistance,
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue)
    {
        if (jumpDistance is null) throw new ArgumentNullException(nameof(jumpDistance));
        if (maxJumps < 0) return Array.Empty<IReadOnlyDictionary<string, object?>>();
        var matched = new List<MissionRecord>();
        foreach (var r in _store.AllMissions)
        {
            if (string.IsNullOrEmpty(r.SourceSystemId)) continue;
            var jumps = jumpDistance(pivotSystemId, r.SourceSystemId!);
            if (jumps < 0 || jumps > maxJumps) continue;
            matched.Add(r);
        }
        return MissionRecordMapper.ToDicts(FilterByAcceptedTime(matched, sinceGameSeconds, untilGameSeconds));
    }

    public IReadOnlyDictionary<string, int> CountByMissionSubclass(
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue) =>
        Tally(FilterByAcceptedTime(_store.AllMissions, sinceGameSeconds, untilGameSeconds),
              r => r.MissionSubclass);

    public IReadOnlyDictionary<string, int> CountByOutcome(
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue) =>
        Tally(FilterByAcceptedTime(_store.AllMissions, sinceGameSeconds, untilGameSeconds)
                .Where(r => r.Outcome.HasValue), r => r.Outcome!.ToString()!);

    public IReadOnlyDictionary<string, int> CountBySystem(
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue) =>
        Tally(FilterByAcceptedTime(_store.AllMissions, sinceGameSeconds, untilGameSeconds)
                .Where(r => !string.IsNullOrEmpty(r.SourceSystemId)), r => r.SourceSystemId!);

    public IReadOnlyDictionary<string, int> CountByFaction(
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue) =>
        Tally(FilterByAcceptedTime(_store.AllMissions, sinceGameSeconds, untilGameSeconds)
                .Where(r => !string.IsNullOrEmpty(r.SourceFaction)), r => r.SourceFaction!);

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> MostActiveSystemsInRange(
        string pivotSystemId, Func<string, string, int> jumpDistance, int maxJumps, int topN,
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue)
    {
        if (jumpDistance is null) throw new ArgumentNullException(nameof(jumpDistance));
        if (topN <= 0 || maxJumps < 0) return Array.Empty<IReadOnlyDictionary<string, object?>>();

        var counts        = new Dictionary<string, int>();
        var jumpsBySystem = new Dictionary<string, int>();
        foreach (var r in FilterByAcceptedTime(_store.AllMissions, sinceGameSeconds, untilGameSeconds))
        {
            if (string.IsNullOrEmpty(r.SourceSystemId)) continue;
            var sys = r.SourceSystemId!;
            if (!jumpsBySystem.TryGetValue(sys, out var jumps))
            {
                jumps = jumpDistance(pivotSystemId, sys);
                jumpsBySystem[sys] = jumps;
            }
            if (jumps < 0 || jumps > maxJumps) continue;
            counts.TryGetValue(sys, out var n);
            counts[sys] = n + 1;
        }
        return counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(topN)
            .Select(kv => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
            {
                ["systemId"] = kv.Key,
                ["count"]    = kv.Value,
                ["jumps"]    = jumpsBySystem[kv.Key],
            })
            .ToList();
    }

    private static IReadOnlyList<MissionRecord> FilterByAcceptedTime(
        IEnumerable<MissionRecord> src, double since, double until)
    {
        var out_ = new List<MissionRecord>();
        foreach (var r in src)
        {
            var at = r.AcceptedAtGameSeconds;
            if (at >= since && at <= until) out_.Add(r);
        }
        return out_;
    }

    private static IReadOnlyDictionary<string, int> Tally(
        IEnumerable<MissionRecord> src, Func<MissionRecord, string> key)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in src)
        {
            var k = key(r);
            result.TryGetValue(k, out var n);
            result[k] = n + 1;
        }
        return result;
    }

    private static bool RecordHasObjective(MissionRecord r, string objectiveType)
    {
        foreach (var step in r.Steps)
            foreach (var obj in step.Objectives)
                if (string.Equals(obj.Type, objectiveType, StringComparison.Ordinal))
                    return true;
        return false;
    }
}
