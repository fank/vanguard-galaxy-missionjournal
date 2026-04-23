using System;
using System.Collections.Generic;

namespace VGMissionLog.Api;

/// <summary>
/// Neutral-shape query interface for cross-mod consumers (spec R4.3).
///
/// <para>Every return type is either a primitive, a string, a
/// <see cref="IReadOnlyList{T}"/> of primitive-keyed dictionaries, or a
/// <see cref="IReadOnlyDictionary{TKey,TValue}"/> of primitives — so
/// reflection-based consumers (VGAnima, future stats dashboards, etc.)
/// can call through <see cref="MissionLogApi.Current"/> without
/// referencing <c>VGMissionLog</c>'s internal record type
/// (<see cref="Logging.ActivityEvent"/>).</para>
///
/// <para>Event dictionaries use the same camelCase keys the JSON sidecar
/// uses (<c>eventId</c>, <c>gameSeconds</c>, <c>storyId</c>, etc.), so
/// consumers can interchange wire-level and API-level access. Fields
/// with no value are either omitted or present with <c>null</c> — both
/// should be handled by downstream code.</para>
///
/// <para>This interface is part of the public API surface: changes to
/// existing methods are breaking (spec R4.2 — major-version bump + doc
/// migration). New methods are additive and require no version bump.</para>
/// </summary>
public interface IMissionLogQuery
{
    /// <summary>Schema version this implementation was built against
    /// (<see cref="Persistence.LogSchema.CurrentVersion"/>). Consumers
    /// feature-gate on this to avoid calling methods added in a newer
    /// version.</summary>
    int SchemaVersion { get; }

    int TotalEventCount { get; }
    double? OldestEventGameSeconds { get; }
    double? NewestEventGameSeconds { get; }

    // --- R2.1 raw filters -------------------------------------------------

    IReadOnlyList<IReadOnlyDictionary<string, object?>> GetEventsInSystem(
        string systemId,
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue);

    IReadOnlyList<IReadOnlyDictionary<string, object?>> GetEventsByFaction(
        string factionId,
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue);

    /// <summary>Filter by raw mission subclass name. The subclass is the
    /// value of <c>mission.GetType().Name</c> captured at event build
    /// time — typically <c>"BountyMission"</c>, <c>"PatrolMission"</c>,
    /// <c>"IndustryMission"</c>, or <c>"Mission"</c> for the base type.
    /// Match is ordinal / case-sensitive; consumers bucket further if
    /// they wish (the log does not classify).</summary>
    IReadOnlyList<IReadOnlyDictionary<string, object?>> GetEventsByMissionSubclass(
        string missionSubclass,
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue);

    /// <summary><paramref name="outcome"/> is one of <c>"Completed"</c> /
    /// <c>"Failed"</c> / <c>"Abandoned"</c>. Non-terminal events never
    /// match.</summary>
    IReadOnlyList<IReadOnlyDictionary<string, object?>> GetEventsByOutcome(
        string outcome,
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue);

    IReadOnlyList<IReadOnlyDictionary<string, object?>> GetEventsForStoryId(string storyId);

    IReadOnlyList<IReadOnlyDictionary<string, object?>> GetRecentEvents(int count);

    // --- R2.2 proximity --------------------------------------------------

    /// <summary>Events within <paramref name="maxJumps"/> of the pivot
    /// system, per the caller-supplied <paramref name="jumpDistance"/>
    /// (signature <c>(from, to) → jumps, -1 if unreachable</c>). Keeps
    /// VGMissionLog graph-agnostic.</summary>
    IReadOnlyList<IReadOnlyDictionary<string, object?>> GetEventsWithinJumps(
        string pivotSystemId,
        int maxJumps,
        Func<string, string, int> jumpDistance,
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue);

    // --- R2.3 aggregates -------------------------------------------------

    /// <summary>Keys are the raw mission subclass name
    /// (<c>mission.GetType().Name</c> at build time — e.g.
    /// <c>"BountyMission"</c>, <c>"Mission"</c>).</summary>
    IReadOnlyDictionary<string, int> CountByMissionSubclass(
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue);

    /// <summary>Keys are one of "Completed" / "Failed" / "Abandoned".</summary>
    IReadOnlyDictionary<string, int> CountByOutcome(
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue);

    IReadOnlyDictionary<string, int> CountBySystem(
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue);

    IReadOnlyDictionary<string, int> CountByFaction(
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue);

    /// <summary>Top-N most active source systems within
    /// <paramref name="maxJumps"/>, sorted by count desc (ordinal
    /// system-id as tiebreaker). Each entry is
    /// <c>{ "systemId": string, "count": int, "jumps": int }</c>.</summary>
    IReadOnlyList<IReadOnlyDictionary<string, object?>> MostActiveSystemsInRange(
        string pivotSystemId,
        Func<string, string, int> jumpDistance,
        int maxJumps,
        int topN,
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue);
}
