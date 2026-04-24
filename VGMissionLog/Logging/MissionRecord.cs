using System;
using System.Collections.Generic;

namespace VGMissionLog.Logging;

/// <summary>
/// One mission's complete record in the log. Identity fields are captured
/// once on <see cref="TimelineState.Accepted"/> and never mutate — vanilla
/// doesn't change a mission after generation. The <see cref="Timeline"/>
/// is the mutable part: it grows by one entry on each lifecycle transition
/// and terminates with exactly one of Completed / Failed / Abandoned.
///
/// <para><b>Identifiers.</b> <see cref="StoryId"/> is populated for authored
/// story missions (Tutorial, Puppeteers); for generator-produced missions
/// vanilla leaves it empty ("") — consumers needing a stable per-record
/// key should use <see cref="MissionInstanceId"/>. <see cref="MissionInstanceId"/>
/// is a session-local GUID (does not survive save/load).</para>
///
/// <para><b>Rewards.</b> One unified list covering every reward subtype. Typed
/// credits/experience/reputation fields from the v1 schema are gone — read
/// them off <see cref="Rewards"/> by <c>Type</c>.</para>
/// </summary>
public sealed record MissionRecord(
    string StoryId,
    string MissionInstanceId,
    string? MissionName,
    string MissionSubclass,
    int MissionLevel,
    string? SourceStationId,
    string? SourceStationName,
    string? SourceSystemId,
    string? SourceSystemName,
    string? SourceSectorId,
    string? SourceSectorName,
    string? SourceFaction,
    string? TargetStationId,
    string? TargetStationName,
    string? TargetSystemId,
    int PlayerLevel,
    string? PlayerShipName,
    int? PlayerShipLevel,
    string? PlayerCurrentSystemId,
    IReadOnlyList<MissionStepDefinition> Steps,
    IReadOnlyList<MissionRewardSnapshot> Rewards,
    IReadOnlyList<TimelineEntry> Timeline)
{
    public bool IsActive => TerminalEntry is null;

    public double AcceptedAtGameSeconds =>
        Timeline.Count > 0 ? Timeline[0].GameSeconds
                           : throw new InvalidOperationException("MissionRecord has no Accepted entry.");

    public double? TerminalAtGameSeconds => TerminalEntry?.GameSeconds;

    public Outcome? Outcome => TerminalEntry?.State switch
    {
        TimelineState.Completed => Logging.Outcome.Completed,
        TimelineState.Failed    => Logging.Outcome.Failed,
        TimelineState.Abandoned => Logging.Outcome.Abandoned,
        _                       => null,
    };

    /// <summary>Age in game-seconds. If the mission has terminated, returns
    /// duration from accept to terminal. If still active, returns
    /// <paramref name="nowGameSeconds"/> − accept.</summary>
    public double AgeSeconds(double nowGameSeconds) =>
        (TerminalAtGameSeconds ?? nowGameSeconds) - AcceptedAtGameSeconds;

    private TimelineEntry? TerminalEntry =>
        Timeline.Count > 0 && Timeline[Timeline.Count - 1].IsTerminal
            ? Timeline[Timeline.Count - 1]
            : null;
}
