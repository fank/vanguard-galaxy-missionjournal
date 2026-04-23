using System;
using System.Collections.Generic;
using System.Reflection;
using Source.Galaxy;
using Source.MissionSystem;
using Source.MissionSystem.Rewards;
using Source.Player;
using VGMissionLog.Classification;

namespace VGMissionLog.Logging;

/// <summary>
/// Snapshots a vanilla <see cref="Mission"/> into a fully-populated
/// <see cref="ActivityEvent"/>. The builder is called from every
/// Phase-4 lifecycle patch — <see cref="MissionAcceptPatch"/>,
/// <see cref="MissionCompletePatch"/>, <see cref="MissionFailPatch"/>,
/// <see cref="MissionAbandonPatch"/>, <see cref="MissionArchivePatch"/>
/// — so each patch body stays ~5 lines of glue.
///
/// <para>Inputs (in addition to the mission + event type) are injected
/// so the builder is deterministic in tests: <see cref="IClock"/> for
/// timestamps, <see cref="Func{GamePlayer}"/> for the "who's playing
/// right now" snapshot (null in tests, <see cref="GamePlayer.current"/>
/// in production).</para>
///
/// <para><b>Publicized-stub access pattern:</b> vanilla public fields
/// work via direct access in both the test stub and the live runtime.
/// Auto-property getters (<c>Mission.rewards</c>, <c>MapElement.guid</c>,
/// <c>Faction.identifier</c>, <c>Faction.name</c>) are replaced by
/// <c>throw null;</c> IL in the publicized stub, so we reflect-read the
/// compiler-synthesised backing fields — same pattern
/// <see cref="ArchetypeInferrer"/> uses. Reflection cost is trivial;
/// each mission lifecycle transition emits at most one event.</para>
/// </summary>
internal sealed class ActivityEventBuilder
{
    private readonly IClock _clock;
    private readonly Func<GamePlayer?> _gamePlayerProvider;

    // --- cached reflection (FieldInfos are immutable and thread-safe once resolved) ---
    private static readonly FieldInfo _missionRewardsField =
        typeof(Mission).GetField("<rewards>k__BackingField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Mission.<rewards>k__BackingField not found — vanilla layout changed?");

    private static readonly FieldInfo _mapElementGuidField =
        typeof(MapElement).GetField("<guid>k__BackingField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("MapElement.<guid>k__BackingField not found — vanilla layout changed?");

    private static readonly FieldInfo _mapElementNameField =
        typeof(MapElement).GetField("_name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("MapElement._name not found — vanilla layout changed?");

    private static readonly FieldInfo _factionIdentifierField =
        typeof(Faction).GetField("<identifier>k__BackingField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Faction.<identifier>k__BackingField not found — vanilla layout changed?");

    public ActivityEventBuilder(IClock clock, Func<GamePlayer?>? gamePlayerProvider = null)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _gamePlayerProvider = gamePlayerProvider ?? (() => GamePlayer.current);
    }

    /// <summary>Build an event for the given mission + lifecycle transition.
    /// Rewards are extracted from <c>mission.rewards</c> only on
    /// <see cref="ActivityEventType.Completed"/>; on other event types those
    /// fields remain null in the event.</summary>
    /// <summary>
    /// Synthesize a <see cref="ActivityEventType.Completed"/> event by
    /// cloning an earlier event for the same storyId. Used by
    /// <see cref="Patches.MissionArchivePatch"/> as a backstop when
    /// <see cref="Patches.MissionCompletePatch"/> failed to fire (rare:
    /// vanilla edge-paths like the dev tutorial-skip, or a swallowed
    /// exception in our own postfix). Rewards are left null because we
    /// didn't see the transition where they'd be captured; everything
    /// else — mission type, source station/system/faction, facility —
    /// carries over from the cloned event.
    /// </summary>
    public ActivityEvent BuildSynthesizedCompleted(ActivityEvent cloneSource) =>
        cloneSource with
        {
            EventId           = Guid.NewGuid().ToString(),
            Type              = ActivityEventType.Completed,
            GameSeconds       = _clock.GameSeconds,
            RealUtc           = _clock.UtcNow.ToString("O"),
            Outcome           = Outcome.Completed,
            RewardsCredits    = null,
            RewardsExperience = null,
            RewardsReputation = null,
        };

    public ActivityEvent Build(Mission mission, ActivityEventType type)
    {
        if (mission is null) throw new ArgumentNullException(nameof(mission));

        var sourcePoi    = mission.sourcePoi;       // public field — works in both envs
        var sourceSystem = sourcePoi?.system;        // public field on MapElement
        var player       = _gamePlayerProvider();
        var extracted    = type == ActivityEventType.Completed
            ? ExtractRewards(mission)
            : default;

        return new ActivityEvent(
            EventId:               Guid.NewGuid().ToString(),
            Type:                  type,
            GameSeconds:           _clock.GameSeconds,
            RealUtc:               _clock.UtcNow.ToString("O"),
            StoryId:               string.IsNullOrEmpty(mission.storyId) ? $"anon:{Guid.NewGuid():N}" : mission.storyId,
            MissionName:           mission.name,     // public field
            MissionType:           MissionClassifier.Classify(mission),
            MissionSubclass:       MissionClassifier.SubclassName(mission),
            MissionLevel:          0,                 // TBD — computed getter depends on GamePlayer.current; MVP ships null-equivalent
            Archetype:             ArchetypeInferrer.Infer(mission),
            Outcome:               DeriveOutcome(type),
            SourceStationId:       ReadGuid(sourcePoi),
            SourceStationName:     ReadName(sourcePoi),
            SourceSystemId:        ReadGuid(sourceSystem),
            SourceSystemName:      ReadName(sourceSystem),
            SourceSectorId:        null,              // TBD — sector probe
            SourceSectorName:      null,
            SourceFaction:         ReadFactionId(mission.sourceFaction),
            TargetStationId:       null,              // TBD — deliver-target extraction (ML-T4h area)
            TargetStationName:     null,
            TargetSystemId:        null,
            FacilityOrigin:        FacilityOriginInferrer.Infer(mission),
            RewardsCredits:        extracted.Credits,
            RewardsExperience:     extracted.Experience,
            RewardsReputation:     extracted.Reputation,
            PlayerLevel:           ReadPlayerLevel(player),
            PlayerShipName:        null,              // TBD — ship probe
            PlayerShipLevel:       null,
            PlayerCurrentSystemId: ReadGuid(player?.currentPointOfInterest?.system));
    }

    // --- outcome derivation ---

    private static Outcome? DeriveOutcome(ActivityEventType type) =>
        type switch
        {
            ActivityEventType.Completed => Outcome.Completed,
            ActivityEventType.Failed    => Outcome.Failed,
            ActivityEventType.Abandoned => Outcome.Abandoned,
            _                           => null,
        };

    // --- reward extraction ---

    private readonly record struct ExtractedRewards(
        long? Credits,
        long? Experience,
        IReadOnlyList<RepReward>? Reputation);

    private static ExtractedRewards ExtractRewards(Mission mission)
    {
        var rewards = _missionRewardsField.GetValue(mission) as List<MissionReward>;
        if (rewards is null || rewards.Count == 0) return default;

        long credits = 0, experience = 0;
        var hasCredits    = false;
        var hasExperience = false;
        List<RepReward>? reputation = null;

        foreach (var reward in rewards)
        {
            switch (reward)
            {
                case Credits c:
                    credits += c.amount;
                    hasCredits = true;
                    break;
                case Experience xp:
                    experience += xp.amount;
                    hasExperience = true;
                    break;
                case Reputation rep:
                    reputation ??= new List<RepReward>();
                    var factionId = ReadFactionId(rep.faction) ?? "unknown";
                    reputation.Add(new RepReward(factionId, rep.amount));
                    break;
            }
        }

        return new ExtractedRewards(
            Credits:    hasCredits    ? credits    : (long?)null,
            Experience: hasExperience ? experience : (long?)null,
            Reputation: reputation);
    }

    // --- reflection-backed field readers (null-safe) ---

    private static string? ReadGuid(MapElement? element) =>
        element is null ? null : _mapElementGuidField.GetValue(element) as string;

    private static string? ReadName(MapElement? element) =>
        element is null ? null : _mapElementNameField.GetValue(element) as string;

    private static string? ReadFactionId(Faction? faction) =>
        faction is null ? null : _factionIdentifierField.GetValue(faction) as string;

    private static int ReadPlayerLevel(GamePlayer? player)
    {
        // GamePlayer.level is computed via commander.level — commander is an
        // auto-property whose getter the stub replaces with `throw null;`.
        // In production (real DLL loaded) it works; here we null-and-default
        // to 0 rather than touching the stubbed getter.
        if (player is null) return 0;
        try { return player.level; }
        catch { return 0; }
    }
}
