using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Behaviour.Item;
using Source.Galaxy;
using Source.MissionSystem;
using Source.MissionSystem.Rewards;
using Source.Player;

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
/// compiler-synthesised backing fields. Reflection cost is trivial;
/// each mission lifecycle transition emits at most one event.</para>
/// </summary>
internal sealed class ActivityEventBuilder
{
    private readonly IClock _clock;
    private readonly Func<GamePlayer?> _gamePlayerProvider;

    // Session-local correlation id per Mission instance. ConditionalWeakTable
    // holds the key weakly, so garbage-collected missions don't keep ids
    // alive. The table lives for the plugin's lifetime (process uptime), but
    // mission identity is lost across save/load because vanilla rebuilds
    // Mission objects on load — that's the caveat documented on
    // ActivityEvent.MissionInstanceId.
    private static readonly ConditionalWeakTable<Mission, string> _instanceIds =
        new();

    // --- cached reflection (FieldInfos are immutable and thread-safe once resolved) ---
    private static readonly FieldInfo _missionRewardsField =
        typeof(Mission).GetField("<rewards>k__BackingField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Mission.<rewards>k__BackingField not found — vanilla layout changed?");

    private static readonly FieldInfo _missionStepsField =
        typeof(Mission).GetField("<steps>k__BackingField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Mission.<steps>k__BackingField not found — vanilla layout changed?");

    private static readonly FieldInfo _stepObjectivesField =
        typeof(MissionStep).GetField("<objectives>k__BackingField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("MissionStep.<objectives>k__BackingField not found — vanilla layout changed?");

    private static readonly FieldInfo _itemTypeIdentifierField =
        typeof(InventoryItemType).GetField("<identifier>k__BackingField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("InventoryItemType.<identifier>k__BackingField not found — vanilla layout changed?");

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

    /// <summary>
    /// Synthesize a <see cref="ActivityEventType.Completed"/> event by
    /// cloning an earlier event for the same storyId. Used by
    /// <see cref="Patches.MissionArchivePatch"/> as a backstop when
    /// <see cref="Patches.MissionCompletePatch"/> failed to fire (rare:
    /// vanilla edge-paths like the dev tutorial-skip, or a swallowed
    /// exception in our own postfix). Rewards are left null because we
    /// didn't see the transition where they'd be captured; everything
    /// else — subclass, source station/system/faction — carries over
    /// from the cloned event.
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
            Rewards           = null,
            // Steps from the acceptance snapshot carry over — the mission
            // object that would let us re-read them is gone by archive time.
        };

    /// <summary>Build an event for the given mission + lifecycle transition.
    /// Rewards are extracted from <c>mission.rewards</c> only on
    /// <see cref="ActivityEventType.Completed"/>; on other event types those
    /// fields remain null in the event.</summary>
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
            StoryId:               mission.storyId ?? string.Empty,
            MissionInstanceId:     GetOrCreateInstanceId(mission),
            MissionName:           mission.name,     // public field
            MissionSubclass:       mission.GetType().Name,
            MissionLevel:          0,                 // TBD — computed getter depends on GamePlayer.current; MVP ships null-equivalent
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
            RewardsCredits:        extracted.Credits,
            RewardsExperience:     extracted.Experience,
            RewardsReputation:     extracted.Reputation,
            Rewards:               extracted.All,
            PlayerLevel:           ReadPlayerLevel(player),
            PlayerShipName:        null,              // TBD — ship probe
            PlayerShipLevel:       null,
            PlayerCurrentSystemId: ReadGuid(player?.currentPointOfInterest?.system),
            Steps:                 ExtractSteps(mission));
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
        IReadOnlyList<RepReward>? Reputation,
        IReadOnlyList<MissionRewardSnapshot>? All);

    private static ExtractedRewards ExtractRewards(Mission mission)
    {
        var rewards = _missionRewardsField.GetValue(mission) as List<MissionReward>;
        if (rewards is null || rewards.Count == 0) return default;

        long credits = 0, experience = 0;
        var hasCredits    = false;
        var hasExperience = false;
        List<RepReward>? reputation = null;
        var all = new List<MissionRewardSnapshot>(rewards.Count);

        foreach (var reward in rewards)
        {
            if (reward is null) continue;

            // Typed fast-path: summed/normalized numerics for the three
            // top-level reward keys (back-compat with the pre-unified schema).
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

            // Unified list: every reward gets a snapshot. Item / Ship /
            // Crew / Skilltree / Skillpoint / WorkshopCredit / StoryMission /
            // MissionFollowUp / POICoordinates / UmbralControl /
            // ConquestStrength are captured here (previously silently dropped).
            all.Add(SnapshotReward(reward));
        }

        return new ExtractedRewards(
            Credits:    hasCredits    ? credits    : (long?)null,
            Experience: hasExperience ? experience : (long?)null,
            Reputation: reputation,
            All:        all.Count == 0 ? null : all);
    }

    private static MissionRewardSnapshot SnapshotReward(MissionReward reward) =>
        new(Type:   reward.GetType().Name,
            Fields: ReadPrimitiveFields(reward));

    // --- mission instance id ---

    private static string GetOrCreateInstanceId(Mission mission) =>
        _instanceIds.GetValue(mission, _ => Guid.NewGuid().ToString());

    // --- step / objective extraction ---

    /// <summary>Snapshot <c>mission.steps</c>. Returns null if the steps
    /// list is inaccessible (reflection-read error) or missing; returns an
    /// empty list if the mission has no steps defined. Consumer-facing
    /// semantics: null = "we couldn't read this", empty = "vanilla has no
    /// steps", non-empty = "here's what we saw".</summary>
    private static IReadOnlyList<MissionStepSnapshot>? ExtractSteps(Mission mission)
    {
        try
        {
            var steps = _missionStepsField.GetValue(mission) as List<MissionStep>;
            if (steps is null) return null;
            var result = new List<MissionStepSnapshot>(steps.Count);
            foreach (var step in steps)
            {
                if (step is null) continue;
                result.Add(SnapshotStep(step));
            }
            return result;
        }
        catch
        {
            return null;
        }
    }

    private static MissionStepSnapshot SnapshotStep(MissionStep step)
    {
        var objectives = _stepObjectivesField.GetValue(step) as List<MissionObjective>;
        var snapshots  = new List<MissionObjectiveSnapshot>(objectives?.Count ?? 0);
        if (objectives != null)
        {
            foreach (var objective in objectives)
            {
                if (objective is null) continue;
                snapshots.Add(SnapshotObjective(objective));
            }
        }

        bool isComplete;
        try { isComplete = step.isComplete; } catch { isComplete = false; }

        return new MissionStepSnapshot(
            Description:          step.description,
            IsComplete:           isComplete,
            RequireAllObjectives: step.requireAllObjectives,
            Hidden:               step.hidden,
            Objectives:           snapshots);
    }

    private static MissionObjectiveSnapshot SnapshotObjective(MissionObjective objective)
    {
        bool isComplete;
        try { isComplete = objective.IsComplete(); } catch { isComplete = false; }

        string? statusText;
        try { statusText = objective.statusText; } catch { statusText = null; }

        return new MissionObjectiveSnapshot(
            Type:       objective.GetType().Name,
            IsComplete: isComplete,
            StatusText: statusText,
            Fields:     ReadPrimitiveFields(objective));
    }

    /// <summary>Reflect across a target's public fields + instance
    /// properties and emit any that are primitive-ish. Enums go through
    /// ToString(). <see cref="Faction"/> / <see cref="InventoryItemType"/>
    /// / <see cref="MapElement"/> references are resolved to their stable
    /// identifier (guid / id / name) via the cached backing-field
    /// readers. Anything else is skipped. Used for both objectives and
    /// rewards — both are open sets of vanilla subclasses with small
    /// amounts of primitive state worth surfacing.</summary>
    private static IReadOnlyDictionary<string, object?>? ReadPrimitiveFields(object target)
    {
        try
        {
            var dict = new Dictionary<string, object?>(capacity: 8);
            var type = target.GetType();

            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                TryAdd(dict, field.Name, SafeGet(() => field.GetValue(target)));
            }
            foreach (var prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                // Skip indexers and write-only; skip display-text getters we
                // either handle separately (statusText) or know are
                // computed/translation-dependent (rewardText/rewardIcon/rewardColor).
                if (prop.GetIndexParameters().Length > 0 || !prop.CanRead) continue;
                switch (prop.Name)
                {
                    case "statusText":
                    case "rewardText":
                    case "rewardIcon":
                    case "rewardColor":
                        continue;
                }
                TryAdd(dict, prop.Name, SafeGet(() => prop.GetValue(target)));
            }
            return dict.Count == 0 ? null : dict;
        }
        catch
        {
            return null;
        }
    }

    private static object? SafeGet(Func<object?> fn)
    {
        try { return fn(); } catch { return null; }
    }

    private static void TryAdd(Dictionary<string, object?> dict, string name, object? value)
    {
        if (value is null) return;
        var camel = ToCamelCase(name);
        if (dict.ContainsKey(camel)) return;

        switch (value)
        {
            case string s:
                dict[camel] = s;
                return;
            case bool or int or long or float or double or short or byte:
                dict[camel] = value;
                return;
            case Enum e:
                dict[camel] = e.ToString();
                return;
            case Faction f:
                var fid = ReadFactionId(f);
                if (fid != null) dict[camel] = fid;
                return;
            case InventoryItemType it:
                var iid = SafeGet(() => _itemTypeIdentifierField.GetValue(it)) as string;
                if (iid != null) dict[camel] = iid;
                return;
            case MapElement me:
                var gid = ReadGuid(me);
                if (gid != null) dict[camel] = gid;
                return;
        }
    }

    private static string ToCamelCase(string s)
    {
        if (string.IsNullOrEmpty(s) || char.IsLower(s[0])) return s;
        return char.ToLowerInvariant(s[0]) + s.Substring(1);
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
