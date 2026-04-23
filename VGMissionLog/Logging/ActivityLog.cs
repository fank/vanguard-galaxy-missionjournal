using System;
using System.Collections.Generic;

namespace VGMissionLog.Logging;

/// <summary>
/// In-memory append-only log of mission activity. Thread-safety is the
/// caller's concern — all vanilla mission hooks we patch run on Unity's
/// main thread, so the plugin never needs locking. Tests exercise the log
/// directly on a single thread.
///
/// Three indexes are maintained eagerly (spec R2 performance budget is
/// 5 ms for 2000 events, easily met with flat indexes):
/// <list type="bullet">
///   <item><c>_byStoryId</c> — for GetEventsForStoryId (full mission timeline)</item>
///   <item><c>_bySourceSystem</c> — for GetEventsInSystem / proximity queries</item>
///   <item><c>_bySourceFaction</c> — for GetEventsByFaction / CountByFaction</item>
/// </list>
/// Index values are direct <see cref="ActivityEvent"/> references so FIFO
/// eviction at the soft cap doesn't invalidate integer positions. The
/// list-based events store trades O(N) head-removal on eviction for
/// simpler invariants — acceptable at N≤2000.
/// </summary>
internal sealed class ActivityLog
{
    public const int DefaultMaxEvents = 2000;

    private readonly int _maxEvents;
    private readonly Action<int>? _onFirstEviction;
    private bool _evictionNotified;

    private readonly List<ActivityEvent> _events = new();
    private readonly Dictionary<string, List<ActivityEvent>> _byStoryId       = new();
    private readonly Dictionary<string, List<ActivityEvent>> _bySourceSystem  = new();
    private readonly Dictionary<string, List<ActivityEvent>> _bySourceFaction = new();

    public ActivityLog(int maxEvents = DefaultMaxEvents, Action<int>? onFirstEviction = null)
    {
        _maxEvents = maxEvents > 0 ? maxEvents : DefaultMaxEvents;
        _onFirstEviction = onFirstEviction;
    }

    /// <summary>
    /// Fired after every successful <see cref="Append"/>. Plugin.Awake
    /// wires this to a BepInEx Debug-level logger when the
    /// <c>Logging.Verbose</c> config flag is on (ML-T6b). Swallowed if it
    /// throws — subscriber failures must not interrupt event capture.
    /// </summary>
    public event Action<ActivityEvent>? OnAppend;

    public int MaxEvents => _maxEvents;

    public int TotalEventCount => _events.Count;

    public IReadOnlyList<ActivityEvent> AllEvents => _events;

    public double? OldestEventGameSeconds =>
        _events.Count == 0 ? null : _events[0].GameSeconds;

    public double? NewestEventGameSeconds =>
        _events.Count == 0 ? null : _events[_events.Count - 1].GameSeconds;

    // --- R2.1 raw filters ---------------------------------------------------
    //
    // All queries return a new list to keep the internal store / index lists
    // encapsulated. At N≤2000 allocating a fresh list per query is trivial.
    // Time-window semantics are inclusive on both bounds — callers typically
    // pass [0, MaxValue] for "no window"; the defaults below match that.

    /// <summary>
    /// Events whose <see cref="ActivityEvent.SourceSystemId"/> matches. Events
    /// without a source system (e.g. synthesized Archive backstops) are not
    /// returned even if the player happened to be in that system.
    /// </summary>
    public IReadOnlyList<ActivityEvent> GetEventsInSystem(
        string systemId,
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue) =>
        FilterByTime(IndexedBySourceSystem(systemId), sinceGameSeconds, untilGameSeconds);

    public IReadOnlyList<ActivityEvent> GetEventsByFaction(
        string factionId,
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue) =>
        FilterByTime(IndexedBySourceFaction(factionId), sinceGameSeconds, untilGameSeconds);

    /// <summary>
    /// Exact <see cref="MissionType"/> match (structural equality on Kind +
    /// Prefix). <c>MissionType.ThirdParty("vganima")</c> matches only vganima
    /// events, not other third-party prefixes.
    /// </summary>
    public IReadOnlyList<ActivityEvent> GetEventsByMissionType(
        MissionType missionType,
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue)
    {
        var result = new List<ActivityEvent>();
        foreach (var evt in _events)
        {
            if (evt.MissionType == missionType &&
                InTimeWindow(evt.GameSeconds, sinceGameSeconds, untilGameSeconds))
            {
                result.Add(evt);
            }
        }
        return result;
    }

    public IReadOnlyList<ActivityEvent> GetEventsByOutcome(
        Outcome outcome,
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue)
    {
        var result = new List<ActivityEvent>();
        foreach (var evt in _events)
        {
            if (evt.Outcome == outcome &&
                InTimeWindow(evt.GameSeconds, sinceGameSeconds, untilGameSeconds))
            {
                result.Add(evt);
            }
        }
        return result;
    }

    /// <summary>
    /// Full per-mission timeline in insertion order (typically
    /// Offered → Accepted → ObjectiveProgressed* → Completed/Failed/Abandoned).
    /// </summary>
    public IReadOnlyList<ActivityEvent> GetEventsForStoryId(string storyId) =>
        new List<ActivityEvent>(IndexedByStoryId(storyId));

    /// <summary>
    /// Up to <paramref name="count"/> events, most-recent-first. Optional
    /// predicate narrows the result set.
    /// </summary>
    public IReadOnlyList<ActivityEvent> GetRecentEvents(
        int count,
        Func<ActivityEvent, bool>? filter = null)
    {
        if (count <= 0) return Array.Empty<ActivityEvent>();
        var result = new List<ActivityEvent>(Math.Min(count, _events.Count));
        for (var i = _events.Count - 1; i >= 0 && result.Count < count; i--)
        {
            var evt = _events[i];
            if (filter is null || filter(evt)) result.Add(evt);
        }
        return result;
    }

    // --- R2.2 proximity queries --------------------------------------------
    //
    // VGMissionLog never walks a jumpgate graph itself — the caller supplies
    // a <c>jumpDistance</c> delegate that returns the number of jumps between
    // two system GUIDs (or -1 when unreachable). This keeps the log
    // graph-agnostic; VGAnima passes its GalaxyDistance.JumpsBetween, a
    // future consumer can pass a different implementation. Pivot-to-pivot
    // (same system) returns 0 jumps by delegate convention.

    /// <summary>
    /// Events whose source system is within <paramref name="maxJumps"/> of
    /// <paramref name="pivotSystemId"/>, as reported by
    /// <paramref name="jumpDistance"/>. Events without a source system are
    /// skipped; unreachable systems (delegate returns &lt; 0) are excluded.
    /// </summary>
    public IReadOnlyList<ActivityEvent> GetEventsWithinJumps(
        string pivotSystemId,
        int maxJumps,
        Func<string, string, int> jumpDistance,
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue)
    {
        if (jumpDistance is null) throw new ArgumentNullException(nameof(jumpDistance));
        if (maxJumps < 0) return Array.Empty<ActivityEvent>();

        var result = new List<ActivityEvent>();
        foreach (var evt in _events)
        {
            if (string.IsNullOrEmpty(evt.SourceSystemId)) continue;
            if (!InTimeWindow(evt.GameSeconds, sinceGameSeconds, untilGameSeconds)) continue;

            var jumps = jumpDistance(pivotSystemId, evt.SourceSystemId!);
            if (jumps < 0 || jumps > maxJumps) continue;

            result.Add(evt);
        }
        return result;
    }

    /// <summary>
    /// Events sorted by ascending jump distance from
    /// <paramref name="pivotSystemId"/>. Each entry carries the event and
    /// the jump count; unreachable events and events without a source
    /// system are excluded. Ties are broken by insertion order.
    /// </summary>
    public IReadOnlyList<(ActivityEvent Event, int Jumps)> GetEventsSortedByJumps(
        string pivotSystemId,
        Func<string, string, int> jumpDistance,
        Func<ActivityEvent, bool>? filter = null)
    {
        if (jumpDistance is null) throw new ArgumentNullException(nameof(jumpDistance));

        var indexed = new List<(ActivityEvent Event, int Jumps, int InsertionOrder)>();
        for (var i = 0; i < _events.Count; i++)
        {
            var evt = _events[i];
            if (string.IsNullOrEmpty(evt.SourceSystemId)) continue;
            if (filter != null && !filter(evt)) continue;

            var jumps = jumpDistance(pivotSystemId, evt.SourceSystemId!);
            if (jumps < 0) continue;

            indexed.Add((evt, jumps, i));
        }

        indexed.Sort((a, b) =>
        {
            var cmp = a.Jumps.CompareTo(b.Jumps);
            return cmp != 0 ? cmp : a.InsertionOrder.CompareTo(b.InsertionOrder);
        });

        var result = new List<(ActivityEvent, int)>(indexed.Count);
        foreach (var (evt, jumps, _) in indexed) result.Add((evt, jumps));
        return result;
    }

    // --- R2.3 aggregate queries --------------------------------------------
    //
    // Counts always consider the source-side of an event (mission type,
    // source system, source faction). Events with null/empty sources are
    // not counted toward any bucket — we prefer an honest "sourceless" gap
    // over synthesizing a catch-all bucket consumers have to know about.

    public IReadOnlyDictionary<MissionType, int> CountByType(
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue)
    {
        var result = new Dictionary<MissionType, int>();
        foreach (var evt in _events)
        {
            if (!InTimeWindow(evt.GameSeconds, sinceGameSeconds, untilGameSeconds)) continue;
            result.TryGetValue(evt.MissionType, out var n);
            result[evt.MissionType] = n + 1;
        }
        return result;
    }

    /// <summary>Counts only terminal events (those carrying an Outcome).</summary>
    public IReadOnlyDictionary<Outcome, int> CountByOutcome(
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue)
    {
        var result = new Dictionary<Outcome, int>();
        foreach (var evt in _events)
        {
            if (evt.Outcome is null) continue;
            if (!InTimeWindow(evt.GameSeconds, sinceGameSeconds, untilGameSeconds)) continue;
            var outcome = evt.Outcome.Value;
            result.TryGetValue(outcome, out var n);
            result[outcome] = n + 1;
        }
        return result;
    }

    public IReadOnlyDictionary<string, int> CountBySystem(
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue)
    {
        var result = new Dictionary<string, int>();
        foreach (var evt in _events)
        {
            if (string.IsNullOrEmpty(evt.SourceSystemId)) continue;
            if (!InTimeWindow(evt.GameSeconds, sinceGameSeconds, untilGameSeconds)) continue;
            var key = evt.SourceSystemId!;
            result.TryGetValue(key, out var n);
            result[key] = n + 1;
        }
        return result;
    }

    public IReadOnlyDictionary<string, int> CountByFaction(
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue)
    {
        var result = new Dictionary<string, int>();
        foreach (var evt in _events)
        {
            if (string.IsNullOrEmpty(evt.SourceFaction)) continue;
            if (!InTimeWindow(evt.GameSeconds, sinceGameSeconds, untilGameSeconds)) continue;
            var key = evt.SourceFaction!;
            result.TryGetValue(key, out var n);
            result[key] = n + 1;
        }
        return result;
    }

    /// <summary>
    /// Top-N most-active source systems within <paramref name="maxJumps"/> of
    /// the pivot, sorted by event count descending. Ties broken by system
    /// id (ordinal) for determinism. Each entry is (systemId, count, jumps).
    /// </summary>
    public IReadOnlyList<(string SystemId, int Count, int Jumps)> MostActiveSystemsInRange(
        string pivotSystemId,
        Func<string, string, int> jumpDistance,
        int maxJumps,
        int topN,
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue)
    {
        if (jumpDistance is null) throw new ArgumentNullException(nameof(jumpDistance));
        if (topN <= 0 || maxJumps < 0) return Array.Empty<(string, int, int)>();

        // One dictionary pass; resolve jump distance lazily per unique system
        // so a long timeline in few systems doesn't re-query the graph N times.
        var counts        = new Dictionary<string, int>();
        var jumpsBySystem = new Dictionary<string, int>();

        foreach (var evt in _events)
        {
            if (string.IsNullOrEmpty(evt.SourceSystemId)) continue;
            if (!InTimeWindow(evt.GameSeconds, sinceGameSeconds, untilGameSeconds)) continue;

            var sys = evt.SourceSystemId!;
            if (!jumpsBySystem.TryGetValue(sys, out var jumps))
            {
                jumps = jumpDistance(pivotSystemId, sys);
                jumpsBySystem[sys] = jumps;
            }
            if (jumps < 0 || jumps > maxJumps) continue;

            counts.TryGetValue(sys, out var n);
            counts[sys] = n + 1;
        }

        var sorted = new List<(string SystemId, int Count, int Jumps)>(counts.Count);
        foreach (var (sys, count) in counts)
        {
            sorted.Add((sys, count, jumpsBySystem[sys]));
        }
        sorted.Sort((a, b) =>
        {
            var cmp = b.Count.CompareTo(a.Count); // descending count
            return cmp != 0 ? cmp : string.CompareOrdinal(a.SystemId, b.SystemId);
        });

        if (sorted.Count > topN) sorted.RemoveRange(topN, sorted.Count - topN);
        return sorted;
    }

    public void Append(ActivityEvent evt)
    {
        if (evt is null) throw new ArgumentNullException(nameof(evt));
        _events.Add(evt);
        IndexEvent(evt);
        if (_events.Count > _maxEvents)
        {
            EvictOldest();
            NotifyFirstEvictionOnce();
        }
        try { OnAppend?.Invoke(evt); } catch { /* subscriber failures must not interrupt capture */ }
    }

    /// <summary>
    /// Replace the log wholesale (used on sidecar load). Clears indexes and
    /// rebuilds from the sequence. Trims from the front if the sequence
    /// exceeds the cap (tolerates an old sidecar that used a higher cap).
    /// </summary>
    public void LoadFrom(IEnumerable<ActivityEvent> events)
    {
        if (events is null) throw new ArgumentNullException(nameof(events));

        _events.Clear();
        _byStoryId.Clear();
        _bySourceSystem.Clear();
        _bySourceFaction.Clear();
        _evictionNotified = false;

        foreach (var evt in events)
        {
            if (evt is null) continue;
            _events.Add(evt);
            IndexEvent(evt);
        }

        while (_events.Count > _maxEvents)
        {
            EvictOldest();
        }
    }

    // Internal index-backed lookups — the surface for ML-T1c query methods.
    // Callers receive a live reference; they must not mutate it.

    internal IReadOnlyList<ActivityEvent> IndexedByStoryId(string storyId) =>
        _byStoryId.TryGetValue(storyId, out var list) ? list : Array.Empty<ActivityEvent>();

    internal IReadOnlyList<ActivityEvent> IndexedBySourceSystem(string systemId) =>
        _bySourceSystem.TryGetValue(systemId, out var list) ? list : Array.Empty<ActivityEvent>();

    internal IReadOnlyList<ActivityEvent> IndexedBySourceFaction(string factionId) =>
        _bySourceFaction.TryGetValue(factionId, out var list) ? list : Array.Empty<ActivityEvent>();

    private void IndexEvent(ActivityEvent evt)
    {
        AddTo(_byStoryId, evt.StoryId, evt);
        if (!string.IsNullOrEmpty(evt.SourceSystemId))  AddTo(_bySourceSystem,  evt.SourceSystemId!,  evt);
        if (!string.IsNullOrEmpty(evt.SourceFaction))   AddTo(_bySourceFaction, evt.SourceFaction!,   evt);
    }

    private void EvictOldest()
    {
        var evicted = _events[0];
        _events.RemoveAt(0);
        RemoveFrom(_byStoryId, evicted.StoryId, evicted);
        if (!string.IsNullOrEmpty(evicted.SourceSystemId))  RemoveFrom(_bySourceSystem,  evicted.SourceSystemId!,  evicted);
        if (!string.IsNullOrEmpty(evicted.SourceFaction))   RemoveFrom(_bySourceFaction, evicted.SourceFaction!,   evicted);
    }

    private void NotifyFirstEvictionOnce()
    {
        if (_evictionNotified) return;
        _evictionNotified = true;
        _onFirstEviction?.Invoke(_maxEvents);
    }

    private static void AddTo(Dictionary<string, List<ActivityEvent>> map, string key, ActivityEvent evt)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<ActivityEvent>();
            map[key] = list;
        }
        list.Add(evt);
    }

    private static void RemoveFrom(Dictionary<string, List<ActivityEvent>> map, string key, ActivityEvent evt)
    {
        if (!map.TryGetValue(key, out var list)) return;
        list.Remove(evt);
        if (list.Count == 0) map.Remove(key);
    }

    private static bool InTimeWindow(double gameSeconds, double since, double until) =>
        gameSeconds >= since && gameSeconds <= until;

    private static IReadOnlyList<ActivityEvent> FilterByTime(
        IReadOnlyList<ActivityEvent> source, double since, double until)
    {
        if (source.Count == 0) return Array.Empty<ActivityEvent>();
        var result = new List<ActivityEvent>(source.Count);
        foreach (var evt in source)
        {
            if (InTimeWindow(evt.GameSeconds, since, until)) result.Add(evt);
        }
        return result;
    }
}
