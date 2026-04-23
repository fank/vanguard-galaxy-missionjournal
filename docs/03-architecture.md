# 03 — Architecture (Proposed Design)

Satisfies the requirements in `02-requirements.md`. The implementor may diverge on specific mechanisms as long as the contract holds.

## Project layout

```
vanguard-galaxy-missionlog/
├── Makefile                  — build / deploy / link-asm / clean / test
├── VGMissionLog.sln
├── VGMissionLog/
│   ├── VGMissionLog.csproj   — netstandard2.1, BepInEx 5, HarmonyX 2.10, Newtonsoft 13
│   ├── lib/                  — (gitignored) symlinked Assembly-CSharp.dll stub
│   ├── Plugin.cs             — BepInPlugin entry point; wires patches + singletons
│   ├── Api/
│   │   └── MissionLogApi.cs  — public static facade for cross-mod consumers
│   ├── Logging/
│   │   ├── ActivityEvent.cs
│   │   ├── ActivityEventType.cs
│   │   ├── MissionType.cs
│   │   ├── Outcome.cs
│   │   ├── FacilityOrigin.cs
│   │   ├── ActivityArchetype.cs
│   │   └── ActivityLog.cs    — in-memory append-only log + query API
│   ├── Classification/
│   │   ├── MissionClassifier.cs   — Mission → (MissionType, Archetype, FacilityOrigin)
│   │   └── StoryIdPrefixMap.cs    — extract a namespace-like prefix from a storyId
│   ├── Persistence/
│   │   ├── LogSchema.cs      — top-level sidecar record; version constant
│   │   ├── LogIO.cs          — atomic read/write + quarantine
│   │   ├── LogPathResolver.cs
│   │   └── DeadSidecarSweeper.cs
│   ├── Patches/
│   │   ├── MissionAcceptPatch.cs
│   │   ├── MissionCompletePatch.cs
│   │   ├── MissionFailPatch.cs
│   │   ├── MissionAbandonPatch.cs
│   │   ├── MissionArchivePatch.cs
│   │   ├── MissionOfferPatches.cs — best-effort Offer hooks
│   │   ├── SaveWritePatch.cs
│   │   └── SaveLoadPatch.cs
│   └── Config/
│       └── MissionLogConfig.cs
├── VGMissionLog.Tests/
│   ├── VGMissionLog.Tests.csproj — net8.0, xunit, DOTNET_ROLL_FORWARD=LatestMajor
│   ├── Logging/      — event + log query tests
│   ├── Persistence/  — IO roundtrip + version + corruption tests
│   ├── Classification/ — MissionClassifier tests
│   └── Api/          — facade surface tests
└── docs/             — this directory
```

## Core data model

```csharp
// Logging/ActivityEvent.cs
public sealed record ActivityEvent(
    string EventId,                  // GUID
    ActivityEventType Type,          // Offered / Accepted / Completed / Failed / Abandoned / ObjectiveProgressed
    double GameSeconds,
    string RealUtc,                  // ISO-8601
    string StoryId,                  // Mission.storyId when present; synthesized when absent
    string? MissionName,
    MissionType MissionType,         // Bounty / Patrol / Industry / Story / Generic / ThirdParty
    string MissionSubclass,          // raw type name
    int MissionLevel,
    ActivityArchetype? Archetype,
    Outcome? Outcome,                // null for non-terminal events
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
    FacilityOrigin? FacilityOrigin,
    long? RewardsCredits,
    long? RewardsExperience,
    IReadOnlyList<RepReward>? RewardsReputation,
    int PlayerLevel,
    string? PlayerShipName,
    int? PlayerShipLevel,
    string? PlayerCurrentSystemId);

public sealed record RepReward(string Faction, int Amount);
```

All fields primitive / nullable-primitive → **no `TypeNameHandling.Auto` needed**, and no `ISerializationBinder` complexity.

## In-memory log

```csharp
// Logging/ActivityLog.cs
internal sealed class ActivityLog
{
    public const int MaxEvents = 2000;
    private readonly List<ActivityEvent> _events = new();
    private readonly Dictionary<string, List<int>> _byStoryId = new(); // for GetEventsForStoryId
    private readonly Dictionary<string, List<int>> _bySystem  = new();
    private readonly Dictionary<string, List<int>> _byFaction = new();
    // ... additional indexes for O(1) filter access

    public void Append(ActivityEvent evt) { /* FIFO evict if over cap; maintain indexes */ }
    public void LoadFrom(IEnumerable<ActivityEvent> events) { /* rebuild indexes */ }
    public IReadOnlyList<ActivityEvent> AllEvents => _events;

    // R2 query methods — see below
}
```

Indexes are rebuilt on load and mutated on append. Query methods use the indexes where advantageous (system / faction / storyId filters) and fall back to linear scan for complex queries. 2000-event linear scans are trivial in practice; the indexes mostly matter because they make per-prompt query bundles (each broker builds ~5 projections at context-build time) not accumulate quadratic cost.

## Query API shape

```csharp
// R2.1 Raw filters — return IReadOnlyList<ActivityEvent>
IReadOnlyList<ActivityEvent> GetEventsInSystem(string systemGuid, double sinceGameSeconds = 0, double untilGameSeconds = double.MaxValue);
IReadOnlyList<ActivityEvent> GetEventsByFaction(string factionId, double sinceGameSeconds, double untilGameSeconds);
// ...

// R2.2 Proximity — consumer passes a graph supplier (VGMissionLog stays graph-agnostic)
IReadOnlyList<ActivityEvent> GetEventsWithinJumps(
    string pivotSystemGuid,
    int maxJumps,
    System.Func<string, string, int> jumpDistance,  // (from, to) → jumps, -1 if unreachable
    double sinceGameSeconds = 0);

// R2.3 Aggregates — return IReadOnlyDictionary
IReadOnlyDictionary<MissionType, int> CountByType(double sinceGameSeconds, double untilGameSeconds);
IReadOnlyDictionary<string, int> CountBySystem(double sinceGameSeconds, double untilGameSeconds);
// ...
```

The proximity queries deliberately take a `jumpDistance` delegate from the caller. VGMissionLog never imports vanilla's galaxy graph or hardcodes a BFS — the caller knows how to walk the graph. Keeps VGMissionLog pure-observational.

## Public facade

```csharp
// Api/MissionLogApi.cs
namespace VGMissionLog.Api;

public static class MissionLogApi
{
    // Set by Plugin.Awake after everything wires up. Null until plugin initialises.
    public static IMissionLogQuery? Current { get; internal set; }
}

// Consumers reflection-probe for this interface or call via `dynamic` on the facade.
public interface IMissionLogQuery
{
    int TotalEventCount { get; }
    int SchemaVersion { get; }
    double? OldestEventGameSeconds { get; }
    double? NewestEventGameSeconds { get; }

    // R2.1, R2.2, R2.3 methods here — signatures must only use primitive types
    // and collection-of-primitive types so reflection callers don't need to
    // reference VGMissionLog's own record types.
    //
    // Two API tiers (implementor's choice):
    //   Tier A (typed): methods return IReadOnlyList<ActivityEvent> etc. —
    //     requires consumers to reference VGMissionLog for the type.
    //   Tier B (neutral): methods return IReadOnlyList<IReadOnlyDictionary<string, object?>> —
    //     no consumer-side type dependency; field access by string key.
    //
    // Recommendation: ship Tier B first (maximum soft-dep compatibility),
    // consider a sidecar `VGMissionLog.Contracts` assembly later for typed access.
}
```

## Harmony hooks

### Required postfixes

All patches follow the same pattern: postfix, exception-swallowed, never-null-check-crashes.

| Patch | Target | Purpose |
|---|---|---|
| `MissionAcceptPatch` | `GamePlayer.AddMissionWithLog(Mission)` | Emit `Accepted` event |
| `MissionCompletePatch` | `GamePlayer.CompleteMission(Mission, bool)` | Emit `Completed` event + capture rewards |
| `MissionFailPatch` | `Mission.MissionFailed(string)` | Emit `Failed` event |
| `MissionAbandonPatch` | `GamePlayer.RemoveMission(Mission, bool)` where `completed=false` | Emit `Abandoned` event |
| `MissionArchivePatch` | `GamePlayer.ArchiveMission(string, bool)` | Backstop — dedup with `Completed` via `EventId` |
| `SaveWritePatch` | `SaveGame.Store` | Flush log to sidecar |
| `SaveLoadPatch` | `SaveGameFile.LoadSaveGame` | Replace in-memory log from sidecar |

### Offer hooks (best-effort)

| Patch | Target | Purpose |
|---|---|---|
| `MissionBoardOfferPatch` | The vanilla method that populates a mission-board panel | Emit `Offered` for board missions |
| `BarSalesmanOfferPatch` | The method that spawns a bar salesman with a mission | Emit `Offered` for bar missions |

Offer hooks are the most vanilla-specific; implementor must scout the decomp at `/tmp/vg-decomp/Assembly-CSharp.decompiled.cs`. If they end up flaky, ship without them at MVP — `Accepted` events are sufficient for most consumer queries.

## Classification

```csharp
// Classification/MissionClassifier.cs
internal static class MissionClassifier
{
    public static MissionType Classify(Mission mission)
    {
        return mission switch
        {
            BountyMission    => MissionType.Bounty,
            PatrolMission    => MissionType.Patrol,
            IndustryMission  => MissionType.Industry,
            StoryMission sm  => ClassifyStory(sm),
            _                => MissionType.Generic,
        };
    }

    private static MissionType ClassifyStory(StoryMission m)
    {
        var id = m.storyId ?? string.Empty;
        var prefix = StoryIdPrefixMap.ExtractPrefix(id);
        return prefix != null
            ? new MissionType.ThirdParty(prefix)
            : MissionType.Story;
    }
}
```

Archetype inference is a best-effort fallback — check vanilla's mission type, inspect step objectives (`KillEnemies` → Combat, `CollectItemTypes(Ore)` → Gather, `CollectItemTypes(Salvage)` → Salvage, `TravelToPOI` → Deliver, `ProtectUnit` → Escort). Unclassifiable → null.

## Persistence

Schema top-level:

```json
{
  "version": 1,
  "events": [
    { "eventId": "...", "type": "Accepted", "gameSeconds": 12345.6, ... },
    ...
  ]
}
```

- `LogSchema.CurrentVersion = 1` at ship time.
- `LogIO.Read` handles: missing file / corrupt JSON / unsupported version. Supports one-version-back auto-upgrade when the change is additive.
- `LogIO.Write` uses the tmp+rename pattern for atomic writes.
- `LogPathResolver.From("<saveName>.save")` → `"<saveName>.save.vgmissionlog.json"`.
- `DeadSidecarSweeper.Sweep()` at plugin load — deletes `.vgmissionlog.json` files whose vanilla save is gone.

## Event emission flow

```
vanilla calls GamePlayer.AddMissionWithLog(mission)
    → Harmony postfix MissionAcceptPatch.Postfix fires
        → MissionClassifier.Classify(mission) → MissionType
        → snapshot source station / system / sector / faction
        → snapshot player state (level, current ship, current system)
        → construct ActivityEvent
        → ActivityLog.Append(event)
        → Plugin.Log.LogDebug(…)  // if Verbose=true
```

## Dependency on vanilla save lifecycle

- `SaveWritePatch` postfix — flush after vanilla's own save succeeds.
- `SaveLoadPatch` prefix — read sidecar **before** vanilla's load finishes, so if any post-load consumer (hypothetical future mod) queries the log during load, it's populated. Prefix not postfix for this timing reason.
- `Application.quitting` handler — flush to `LastKnownSavePath` as a safety net.

## Testing approach

`.NET 8` test project targeting netstandard2.1 plugin, using `DOTNET_ROLL_FORWARD=LatestMajor` to load the netstandard2.1 binary under net8.0. Reference the publicized `Assembly-CSharp.dll` symlinked stub via `make link-asm`.

- **Logging tests** — pure in-memory log, deterministic timestamps via injected `IClock`.
- **Persistence tests** — temp-directory file IO, roundtrip + corrupt + version cases.
- **Classification tests** — construct fake `Mission` subclasses (or the minimum vanilla-type surface needed) and assert `MissionClassifier` output.
- **API facade tests** — set up a synthetic `ActivityLog`, probe the `MissionLogApi.Current` interface.

## Out of scope for MVP

- Objective-progression events (`ObjectiveProgressed`) — spec'd but ship without.
- JSON export tool.
- UI.
- Typed `VGMissionLog.Contracts` assembly (reflection API is sufficient initially).
- Cross-save migration tooling (each save's log is independent; no migration path needed).
