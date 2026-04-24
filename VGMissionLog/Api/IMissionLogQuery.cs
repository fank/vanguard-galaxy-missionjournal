using System;
using System.Collections.Generic;

namespace VGMissionLog.Api;

/// <summary>
/// Neutral-shape query interface for cross-mod consumers. v3 is mission-oriented:
/// every return is either a single mission dict, a list of mission dicts, or a
/// primitive-keyed aggregate. Fields omitted or null should both be handled.
///
/// <para>Each "mission dict" uses the same camelCase keys as the sidecar's
/// <c>missions[]</c> entry: <c>storyId</c>, <c>missionInstanceId</c>,
/// <c>missionSubclass</c>, <c>steps</c>, <c>rewards</c>, <c>timeline</c>, etc.
/// Age/active/outcome are derived off <c>timeline[]</c> by the consumer, or
/// read off the helper fields the mapper exposes (<c>acceptedAtGameSeconds</c>,
/// <c>terminalAtGameSeconds</c>, <c>outcome</c>, <c>isActive</c>).</para>
///
/// <para>Adding new methods is non-breaking; changing existing signatures is.</para>
/// </summary>
public interface IMissionLogQuery
{
    int SchemaVersion { get; }
    int TotalMissionCount { get; }
    double? OldestAcceptedGameSeconds { get; }
    double? NewestAcceptedGameSeconds { get; }

    IReadOnlyDictionary<string, object?>? GetMission(string missionInstanceId);
    IReadOnlyList<IReadOnlyDictionary<string, object?>> GetActiveMissions();
    IReadOnlyList<IReadOnlyDictionary<string, object?>> GetAllMissions();

    IReadOnlyList<IReadOnlyDictionary<string, object?>> GetMissionsInSystem(
        string systemId, double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue);

    IReadOnlyList<IReadOnlyDictionary<string, object?>> GetMissionsByFaction(
        string factionId, double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue);

    IReadOnlyList<IReadOnlyDictionary<string, object?>> GetMissionsByMissionSubclass(
        string missionSubclass, double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue);

    /// <summary><paramref name="outcome"/> is one of <c>"Completed"</c> /
    /// <c>"Failed"</c> / <c>"Abandoned"</c>. Active missions never match.</summary>
    IReadOnlyList<IReadOnlyDictionary<string, object?>> GetMissionsByOutcome(
        string outcome, double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue);

    /// <summary>Missions whose <c>steps[].objectives[].type</c> includes
    /// <paramref name="objectiveType"/>. Case-sensitive ordinal.</summary>
    IReadOnlyList<IReadOnlyDictionary<string, object?>> GetMissionsWithObjective(
        string objectiveType, double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue);

    /// <summary>Missions for a specific storyId (story missions that share an id
    /// across accept/archive chains). Returns 0..N records depending on how
    /// many mission instances carried that id.</summary>
    IReadOnlyList<IReadOnlyDictionary<string, object?>> GetMissionsForStoryId(string storyId);

    /// <summary>Up to <paramref name="count"/> missions sorted by accept time
    /// descending (newest first).</summary>
    IReadOnlyList<IReadOnlyDictionary<string, object?>> GetRecentMissions(int count);

    // --- Proximity -------------------------------------------------------

    IReadOnlyList<IReadOnlyDictionary<string, object?>> GetMissionsWithinJumps(
        string pivotSystemId,
        int maxJumps,
        Func<string, string, int> jumpDistance,
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue);

    // --- Aggregates ------------------------------------------------------

    IReadOnlyDictionary<string, int> CountByMissionSubclass(
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue);

    IReadOnlyDictionary<string, int> CountByOutcome(
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue);

    IReadOnlyDictionary<string, int> CountBySystem(
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue);

    IReadOnlyDictionary<string, int> CountByFaction(
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue);

    /// <summary>Top-N most-active source systems within <paramref name="maxJumps"/>.</summary>
    IReadOnlyList<IReadOnlyDictionary<string, object?>> MostActiveSystemsInRange(
        string pivotSystemId,
        Func<string, string, int> jumpDistance,
        int maxJumps,
        int topN,
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue);
}
