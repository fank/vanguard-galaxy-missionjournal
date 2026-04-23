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

    public int MaxEvents => _maxEvents;

    public int TotalEventCount => _events.Count;

    public IReadOnlyList<ActivityEvent> AllEvents => _events;

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
}
