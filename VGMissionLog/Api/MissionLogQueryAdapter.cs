using System;
using System.Collections.Generic;
using VGMissionLog.Logging;
using VGMissionLog.Persistence;

namespace VGMissionLog.Api;

/// <summary>
/// Production <see cref="IMissionLogQuery"/> implementation — wraps the
/// internal <see cref="ActivityLog"/> and adapts its typed return values
/// into the neutral primitive-keyed dictionary shape consumers see.
/// </summary>
internal sealed class MissionLogQueryAdapter : IMissionLogQuery
{
    private readonly ActivityLog _log;

    public MissionLogQueryAdapter(ActivityLog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public int SchemaVersion         => LogSchema.CurrentVersion;
    public int TotalEventCount       => _log.TotalEventCount;
    public double? OldestEventGameSeconds => _log.OldestEventGameSeconds;
    public double? NewestEventGameSeconds => _log.NewestEventGameSeconds;

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> GetEventsInSystem(
        string systemId, double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue) =>
        ActivityEventMapper.ToDicts(_log.GetEventsInSystem(systemId, sinceGameSeconds, untilGameSeconds));

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> GetEventsByFaction(
        string factionId, double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue) =>
        ActivityEventMapper.ToDicts(_log.GetEventsByFaction(factionId, sinceGameSeconds, untilGameSeconds));

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> GetEventsByMissionSubclass(
        string missionSubclass,
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue) =>
        ActivityEventMapper.ToDicts(_log.GetEventsByMissionSubclass(
            missionSubclass, sinceGameSeconds, untilGameSeconds));

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> GetEventsByOutcome(
        string outcome, double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue)
    {
        if (!Enum.TryParse<Outcome>(outcome, ignoreCase: false, out var parsed))
            return Array.Empty<IReadOnlyDictionary<string, object?>>();
        return ActivityEventMapper.ToDicts(_log.GetEventsByOutcome(parsed, sinceGameSeconds, untilGameSeconds));
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> GetEventsForStoryId(string storyId) =>
        ActivityEventMapper.ToDicts(_log.GetEventsForStoryId(storyId));

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> GetRecentEvents(int count) =>
        ActivityEventMapper.ToDicts(_log.GetRecentEvents(count));

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> GetEventsWithinJumps(
        string pivotSystemId, int maxJumps, Func<string, string, int> jumpDistance,
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue) =>
        ActivityEventMapper.ToDicts(_log.GetEventsWithinJumps(
            pivotSystemId, maxJumps, jumpDistance, sinceGameSeconds, untilGameSeconds));

    public IReadOnlyDictionary<string, int> CountByMissionSubclass(
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue) =>
        _log.CountByMissionSubclass(sinceGameSeconds, untilGameSeconds);

    public IReadOnlyDictionary<string, int> CountByOutcome(
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue)
    {
        var raw = _log.CountByOutcome(sinceGameSeconds, untilGameSeconds);
        var result = new Dictionary<string, int>(raw.Count);
        foreach (var (outcome, n) in raw) result[outcome.ToString()] = n;
        return result;
    }

    public IReadOnlyDictionary<string, int> CountBySystem(
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue) =>
        _log.CountBySystem(sinceGameSeconds, untilGameSeconds);

    public IReadOnlyDictionary<string, int> CountByFaction(
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue) =>
        _log.CountByFaction(sinceGameSeconds, untilGameSeconds);

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> MostActiveSystemsInRange(
        string pivotSystemId, Func<string, string, int> jumpDistance, int maxJumps, int topN,
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue)
    {
        var raw = _log.MostActiveSystemsInRange(
            pivotSystemId, jumpDistance, maxJumps, topN, sinceGameSeconds, untilGameSeconds);
        var result = new List<IReadOnlyDictionary<string, object?>>(raw.Count);
        foreach (var (sys, count, jumps) in raw)
        {
            result.Add(new Dictionary<string, object?>
            {
                ["systemId"] = sys,
                ["count"]    = count,
                ["jumps"]    = jumps,
            });
        }
        return result;
    }
}
