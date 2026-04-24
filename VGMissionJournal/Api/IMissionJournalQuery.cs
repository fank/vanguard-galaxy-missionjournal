using System;
using System.Collections.Generic;
using VGMissionJournal.Logging;

namespace VGMissionJournal.Api;

/// <summary>
/// Public query surface for mission-log consumers. Returns typed
/// <see cref="MissionRecord"/> aggregates — every field is typed, field
/// renames are compile-time errors.
///
/// <para>This is the contract: changes to existing method signatures are
/// breaking (major version + migration note). New methods are additive.
/// Changes to <see cref="MissionRecord"/>'s property set are likewise
/// breaking; <see cref="SchemaVersion"/> reflects the sidecar wire format,
/// which is a separate concern from the C# API contract.</para>
/// </summary>
public interface IMissionJournalQuery
{
    int SchemaVersion { get; }
    int TotalMissionCount { get; }
    double? OldestAcceptedGameSeconds { get; }
    double? NewestAcceptedGameSeconds { get; }

    MissionRecord? GetMission(string missionInstanceId);
    IReadOnlyList<MissionRecord> GetActiveMissions();
    IReadOnlyList<MissionRecord> GetAllMissions();

    IReadOnlyList<MissionRecord> GetMissionsInSystem(
        string systemId,
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue);

    IReadOnlyList<MissionRecord> GetMissionsByFaction(
        string factionId,
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue);

    IReadOnlyList<MissionRecord> GetMissionsByMissionSubclass(
        string missionSubclass,
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue);

    /// <summary>Active missions never match. Pass <see cref="Outcome.Completed"/>
    /// etc. — typed, not strings.</summary>
    IReadOnlyList<MissionRecord> GetMissionsByOutcome(
        Outcome outcome,
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue);

    /// <summary>Missions whose <c>Steps[].Objectives[].Type</c> contains
    /// <paramref name="objectiveType"/>. Case-sensitive ordinal.</summary>
    IReadOnlyList<MissionRecord> GetMissionsWithObjective(
        string objectiveType,
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue);

    /// <summary>Missions for a specific storyId (story missions that share an id
    /// across accept/archive chains). Returns 0..N records depending on how
    /// many mission instances carried that id.</summary>
    IReadOnlyList<MissionRecord> GetMissionsForStoryId(string storyId);

    /// <summary>Up to <paramref name="count"/> missions sorted by accept time
    /// descending (newest first).</summary>
    IReadOnlyList<MissionRecord> GetRecentMissions(int count);

    // --- Proximity -------------------------------------------------------

    IReadOnlyList<MissionRecord> GetMissionsWithinJumps(
        string pivotSystemId,
        int maxJumps,
        Func<string, string, int> jumpDistance,
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue);

    // --- Aggregates ------------------------------------------------------

    IReadOnlyDictionary<string, int> CountByMissionSubclass(
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue);

    /// <summary>Keys are <see cref="Outcome"/> enum values (typed, not strings).</summary>
    IReadOnlyDictionary<Outcome, int> CountByOutcome(
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue);

    IReadOnlyDictionary<string, int> CountBySystem(
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue);

    IReadOnlyDictionary<string, int> CountByFaction(
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue);

    /// <summary>Top-N most-active source systems within <paramref name="maxJumps"/>.</summary>
    IReadOnlyList<SystemActivity> MostActiveSystemsInRange(
        string pivotSystemId,
        Func<string, string, int> jumpDistance,
        int maxJumps,
        int topN,
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue);
}

/// <summary>Entry in <see cref="IMissionJournalQuery.MostActiveSystemsInRange"/>
/// result.</summary>
public sealed record SystemActivity(string SystemId, int Count, int Jumps);
