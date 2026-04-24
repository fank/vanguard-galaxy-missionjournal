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

    public MissionRecord? GetMission(string missionInstanceId) =>
        _store.GetByInstanceId(missionInstanceId);

    public IReadOnlyList<MissionRecord> GetActiveMissions() =>
        _store.GetActiveMissions();

    public IReadOnlyList<MissionRecord> GetAllMissions() =>
        _store.AllMissions;

    public IReadOnlyList<MissionRecord> GetMissionsInSystem(
        string systemId, double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue) =>
        FilterByAcceptedTime(_store.GetMissionsInSystem(systemId), sinceGameSeconds, untilGameSeconds);

    public IReadOnlyList<MissionRecord> GetMissionsByFaction(
        string factionId, double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue) =>
        FilterByAcceptedTime(_store.GetMissionsByFaction(factionId), sinceGameSeconds, untilGameSeconds);

    public IReadOnlyList<MissionRecord> GetMissionsByMissionSubclass(
        string missionSubclass, double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue)
    {
        var matched = _store.AllMissions.Where(r =>
            string.Equals(r.MissionSubclass, missionSubclass, StringComparison.Ordinal)).ToList();
        return FilterByAcceptedTime(matched, sinceGameSeconds, untilGameSeconds);
    }

    public IReadOnlyList<MissionRecord> GetMissionsByOutcome(
        Outcome outcome, double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue)
    {
        var matched = _store.AllMissions.Where(r => r.Outcome == outcome).ToList();
        return FilterByAcceptedTime(matched, sinceGameSeconds, untilGameSeconds);
    }

    public IReadOnlyList<MissionRecord> GetMissionsWithObjective(
        string objectiveType, double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue)
    {
        if (string.IsNullOrEmpty(objectiveType))
            return Array.Empty<MissionRecord>();
        var matched = _store.AllMissions.Where(r => RecordHasObjective(r, objectiveType)).ToList();
        return FilterByAcceptedTime(matched, sinceGameSeconds, untilGameSeconds);
    }

    public IReadOnlyList<MissionRecord> GetMissionsForStoryId(string storyId) =>
        _store.AllMissions
            .Where(r => string.Equals(r.StoryId, storyId, StringComparison.Ordinal))
            .ToList();

    public IReadOnlyList<MissionRecord> GetRecentMissions(int count)
    {
        if (count <= 0) return Array.Empty<MissionRecord>();
        return _store.AllMissions
            .OrderByDescending(r => r.AcceptedAtGameSeconds)
            .Take(count)
            .ToList();
    }

    public IReadOnlyList<MissionRecord> GetMissionsWithinJumps(
        string pivotSystemId, int maxJumps, Func<string, string, int> jumpDistance,
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue)
    {
        if (jumpDistance is null) throw new ArgumentNullException(nameof(jumpDistance));
        if (maxJumps < 0) return Array.Empty<MissionRecord>();
        var matched = new List<MissionRecord>();
        foreach (var r in _store.AllMissions)
        {
            if (string.IsNullOrEmpty(r.SourceSystemId)) continue;
            var jumps = jumpDistance(pivotSystemId, r.SourceSystemId!);
            if (jumps < 0 || jumps > maxJumps) continue;
            matched.Add(r);
        }
        return FilterByAcceptedTime(matched, sinceGameSeconds, untilGameSeconds);
    }

    public IReadOnlyDictionary<string, int> CountByMissionSubclass(
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue) =>
        Tally(FilterByAcceptedTime(_store.AllMissions, sinceGameSeconds, untilGameSeconds),
              r => r.MissionSubclass);

    public IReadOnlyDictionary<Outcome, int> CountByOutcome(
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue)
    {
        var result = new Dictionary<Outcome, int>();
        foreach (var r in FilterByAcceptedTime(_store.AllMissions, sinceGameSeconds, untilGameSeconds))
        {
            if (r.Outcome is not Outcome o) continue;
            result.TryGetValue(o, out var n);
            result[o] = n + 1;
        }
        return result;
    }

    public IReadOnlyDictionary<string, int> CountBySystem(
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue) =>
        Tally(FilterByAcceptedTime(_store.AllMissions, sinceGameSeconds, untilGameSeconds)
                .Where(r => !string.IsNullOrEmpty(r.SourceSystemId)), r => r.SourceSystemId!);

    public IReadOnlyDictionary<string, int> CountByFaction(
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue) =>
        Tally(FilterByAcceptedTime(_store.AllMissions, sinceGameSeconds, untilGameSeconds)
                .Where(r => !string.IsNullOrEmpty(r.SourceFaction)), r => r.SourceFaction!);

    public IReadOnlyList<SystemActivity> MostActiveSystemsInRange(
        string pivotSystemId, Func<string, string, int> jumpDistance, int maxJumps, int topN,
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue)
    {
        if (jumpDistance is null) throw new ArgumentNullException(nameof(jumpDistance));
        if (topN <= 0 || maxJumps < 0) return Array.Empty<SystemActivity>();

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
            .Select(kv => new SystemActivity(kv.Key, kv.Value, jumpsBySystem[kv.Key]))
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
