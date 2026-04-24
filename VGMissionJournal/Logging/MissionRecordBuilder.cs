using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Behaviour.Item;
using Source.Galaxy;
using Source.MissionSystem;
using Source.MissionSystem.Rewards;
using Source.Player;

namespace VGMissionJournal.Logging;

/// <summary>
/// Snapshots a vanilla <see cref="Mission"/> into a <see cref="MissionRecord"/>
/// and appends timeline entries as the mission progresses through its lifecycle.
///
/// <para>The builder is called from Phase-4 lifecycle patches —
/// <see cref="Patches.MissionAcceptPatch"/>,
/// <see cref="Patches.MissionCompletePatch"/>,
/// <see cref="Patches.MissionFailPatch"/>,
/// <see cref="Patches.MissionAbandonPatch"/>,
/// <see cref="Patches.MissionArchivePatch"/>
/// — so each patch body stays ~5 lines of glue.</para>
///
/// <para>Inputs are injected so the builder is deterministic in tests:
/// <see cref="IClock"/> for timestamps,
/// <see cref="Func{String}"/> for the player's current system id.</para>
///
/// <para><b>Publicized-stub access pattern:</b> vanilla public fields
/// work via direct access in both the test stub and the live runtime.
/// Auto-property getters (<c>Mission.rewards</c>, <c>MapElement.guid</c>,
/// <c>Faction.identifier</c>, <c>Faction.name</c>) are replaced by
/// <c>throw null;</c> IL in the publicized stub, so we reflect-read the
/// compiler-synthesised backing fields. Reflection cost is trivial;
/// each mission lifecycle transition emits at most one event.</para>
/// </summary>
internal sealed class MissionRecordBuilder
{
    private readonly IClock _clock;
    private readonly Func<string?> _playerCurrentSystemIdProvider;

    // Session-local correlation id per Mission instance. ConditionalWeakTable
    // holds the key weakly, so garbage-collected missions don't keep ids
    // alive. The table lives for the plugin's lifetime (process uptime), but
    // mission identity is lost across save/load because vanilla rebuilds
    // Mission objects on load.
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

    public MissionRecordBuilder(IClock clock, Func<string?> playerCurrentSystemIdProvider)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _playerCurrentSystemIdProvider = playerCurrentSystemIdProvider ?? throw new ArgumentNullException(nameof(playerCurrentSystemIdProvider));
    }

    /// <summary>Session-local GUID per Mission instance. Uses a
    /// ConditionalWeakTable keyed by the Mission reference, so two
    /// lookups for the same Mission return the same id.</summary>
    public string GetInstanceId(Mission mission) =>
        _instanceIds.GetValue(mission, _ => Guid.NewGuid().ToString());

    /// <summary>Snapshot identity + structure (steps) + rewards of an
    /// accepted mission. Returns a MissionRecord with a single timeline
    /// entry: Accepted, stamped with the current clock.</summary>
    public MissionRecord CreateFromAccept(Mission mission)
    {
        if (mission is null) throw new ArgumentNullException(nameof(mission));

        var sourcePoi    = mission.sourcePoi;       // public field — works in both envs
        var sourceSystem = sourcePoi?.system;        // public field on MapElement

        var timeline = new List<TimelineEntry>
        {
            new TimelineEntry(TimelineState.Accepted, _clock.GameSeconds, _clock.UtcNow.ToString("o")),
        };

        return new MissionRecord(
            StoryId:                 mission.storyId ?? string.Empty,
            MissionInstanceId:       GetInstanceId(mission),
            MissionName:             mission.name,
            MissionSubclass:         mission.GetType().Name,
            MissionLevel:            0,
            SourceStationId:         ReadGuid(sourcePoi),
            SourceStationName:       ReadName(sourcePoi),
            SourceSystemId:          ReadGuid(sourceSystem),
            SourceSystemName:        ReadName(sourceSystem),
            SourceSectorId:          null,
            SourceSectorName:        null,
            SourceFaction:           ReadFactionId(mission.sourceFaction),
            TargetStationId:         null,
            TargetStationName:       null,
            TargetSystemId:          null,
            PlayerLevel:             0,
            PlayerShipName:          null,
            PlayerShipLevel:         null,
            PlayerCurrentSystemId:   _playerCurrentSystemIdProvider(),
            Steps:                   ExtractSteps(mission),
            Rewards:                 ExtractRewards(mission),
            Timeline:                timeline);
    }

    /// <summary>Append a new timeline entry to an existing record. For
    /// terminal states (Completed/Failed/Abandoned), re-extract rewards
    /// off the live Mission (null means skip reward re-extract, used by
    /// Archive backstop). Returns a new record (records are immutable).</summary>
    public MissionRecord AppendTransition(
        MissionRecord existing,
        TimelineState state,
        Mission? mission)
    {
        if (existing is null) throw new ArgumentNullException(nameof(existing));

        var newEntry = new TimelineEntry(state, _clock.GameSeconds, _clock.UtcNow.ToString("o"));
        var newTimeline = new List<TimelineEntry>(existing.Timeline) { newEntry };

        // Re-read rewards on Completed with a live mission (vanilla populates
        // mission.rewards during ClaimRewards; our postfix may see the final set).
        // Failed/Abandoned don't pay rewards; null mission means we can't re-read.
        var newRewards = (state == TimelineState.Completed && mission is not null)
            ? ExtractRewards(mission)
            : existing.Rewards;

        return existing with { Timeline = newTimeline, Rewards = newRewards };
    }

    // --- reward extraction ---

    private static IReadOnlyList<MissionRewardSnapshot> ExtractRewards(Mission mission)
    {
        try
        {
            var rewards = _missionRewardsField.GetValue(mission) as List<MissionReward>;
            if (rewards is null || rewards.Count == 0) return Array.Empty<MissionRewardSnapshot>();

            var all = new List<MissionRewardSnapshot>(rewards.Count);
            foreach (var reward in rewards)
            {
                if (reward is null) continue;
                all.Add(SnapshotReward(reward));
            }
            return all.Count == 0 ? Array.Empty<MissionRewardSnapshot>() : all;
        }
        catch
        {
            return Array.Empty<MissionRewardSnapshot>();
        }
    }

    private static MissionRewardSnapshot SnapshotReward(MissionReward reward) =>
        new(Type:   reward.GetType().Name,
            Fields: ReadPrimitiveFields(reward));

    // --- step / objective extraction ---

    /// <summary>Snapshot <c>mission.steps</c>. Returns an empty list if the steps
    /// list is inaccessible (reflection-read error) or missing. Consumer-facing
    /// semantics: empty = "vanilla has no steps or we couldn't read them",
    /// non-empty = "here's what we saw".</summary>
    private static IReadOnlyList<MissionStepDefinition> ExtractSteps(Mission mission)
    {
        try
        {
            var steps = _missionStepsField.GetValue(mission) as List<MissionStep>;
            if (steps is null) return Array.Empty<MissionStepDefinition>();
            var result = new List<MissionStepDefinition>(steps.Count);
            foreach (var step in steps)
            {
                if (step is null) continue;
                result.Add(SnapshotStep(step));
            }
            return result.Count == 0 ? Array.Empty<MissionStepDefinition>() : result;
        }
        catch
        {
            return Array.Empty<MissionStepDefinition>();
        }
    }

    private static MissionStepDefinition SnapshotStep(MissionStep step)
    {
        var objectives = _stepObjectivesField.GetValue(step) as List<MissionObjective>;
        var defs = new List<MissionObjectiveDefinition>(objectives?.Count ?? 0);
        if (objectives != null)
        {
            foreach (var objective in objectives)
            {
                if (objective is null) continue;
                defs.Add(SnapshotObjective(objective));
            }
        }

        return new MissionStepDefinition(
            Description:          step.description,
            RequireAllObjectives: step.requireAllObjectives,
            Hidden:               step.hidden,
            Objectives:           defs);
    }

    private static MissionObjectiveDefinition SnapshotObjective(MissionObjective objective) =>
        new(Type:   objective.GetType().Name,
            Fields: ReadPrimitiveFields(objective));

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
                // either handle separately or know are computed/translation-dependent.
                if (prop.GetIndexParameters().Length > 0 || !prop.CanRead) continue;
                switch (prop.Name)
                {
                    case "statusText":    // objective user-visible (v3 drops this from the definition)
                    case "rewardText":    // reward user-visible (translation-dependent)
                    case "rewardIcon":
                    case "rewardColor":
                    case "coreName":      // MissionObjective base getter returning "Core" verbatim — pure noise
                    case "currentAmount": // live progress counter — not mission structure
                    case "displayedAmount": // same, UI-rendered count
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
                var iid = ResolveItemIdentifier(it);
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

    /// <summary>
    /// Resolve an <see cref="InventoryItemType"/> to its stable registry
    /// identifier — the string <see cref="InventoryItemType.Get"/> accepts.
    /// Vanilla's load code sets <c>identifier = name</c> once on the prefab
    /// (<c>InventoryItemType.cs:717</c>), and <c>identifier</c> is an
    /// auto-property backing field without <c>[SerializeField]</c>, so
    /// <see cref="UnityEngine.Object.Instantiate(UnityEngine.Object)"/> does
    /// NOT carry the value to runtime clones (<c>Item</c>-reward instances
    /// produced by <c>ItemBuilder.CreateItemType</c>). For those clones the
    /// stable id lives on the Unity object's <c>name</c> — with the
    /// <c>(Clone)</c> suffix that <c>Instantiate</c> appends stripped.
    /// Translated <c>displayName</c> is intentionally NOT used; the log
    /// stores system identifiers so consumers can round-trip via <c>Get</c>.
    /// </summary>
    private static string? ResolveItemIdentifier(InventoryItemType it)
    {
        if (it == null) return null;

        var backing = SafeGet(() => _itemTypeIdentifierField.GetValue(it)) as string;
        if (!string.IsNullOrEmpty(backing)) return backing;

        var name = SafeGet(() => it.name) as string;
        return StripCloneSuffix(name);
    }

    /// <summary>Strip Unity's "(Clone)" suffix (sometimes stacked for nested
    /// Instantiate calls, sometimes with a leading space) to recover the
    /// stable registry key. Internal + visible-to-tests for direct
    /// coverage of the string-manipulation path, since the surrounding
    /// <see cref="ResolveItemIdentifier"/> needs a real
    /// <see cref="UnityEngine.Object"/> instance that isn't
    /// constructible in the xUnit runtime.</summary>
    internal static string? StripCloneSuffix(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        const string cloneSuffix = "(Clone)";
        while (name!.EndsWith(cloneSuffix, System.StringComparison.Ordinal))
        {
            name = name.Substring(0, name.Length - cloneSuffix.Length).TrimEnd();
        }
        return string.IsNullOrEmpty(name) ? null : name;
    }
}
