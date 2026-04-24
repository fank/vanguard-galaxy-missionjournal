# Consumer API

Everything a modder needs to read mission history from VGMissionLog. If you want to integrate, start here.

## Integration

Two paths, same behaviour. Pick one.

### Typed reference (recommended)

Drop `VGMissionLog.dll` into your plugin's `libs/` folder and add a `<Reference>` in your csproj. Then soft-dep on the plugin GUID and put API usage behind a presence-check so your mod still loads when VGMissionLog isn't installed:

```csharp
using BepInEx;
using BepInEx.Bootstrap;
using VGMissionLog.Api;

[BepInPlugin("my.consumer", "My Consumer", "1.0.0")]
[BepInDependency("vgmissionlog", BepInDependency.DependencyFlags.SoftDependency)]
public class MyPlugin : BaseUnityPlugin
{
    private void Awake()
    {
        if (Chainloader.PluginInfos.ContainsKey("vgmissionlog"))
            UseMissionLog();
    }

    // Separate method so the JIT doesn't resolve VGMissionLog types
    // until this branch is actually taken (important when the plugin
    // isn't installed — referenced assembly would otherwise fail to load).
    private void UseMissionLog()
    {
        if (MissionLogApi.Current is not { } api) return;
        foreach (var m in api.GetRecentMissions(10))
            Logger.LogInfo($"{m["missionSubclass"]} {m["outcome"] ?? "active"}");
    }
}
```

### Reflection fallback

For scripting-style mods that can't add a compile-time reference:

```csharp
var facade = Type.GetType("VGMissionLog.Api.MissionLogApi, VGMissionLog");
var api    = facade?.GetProperty("Current")?.GetValue(null);
if (api is null) return;  // plugin not installed

var method   = api.GetType().GetMethod("GetRecentMissions", new[] { typeof(int) });
var missions = (IReadOnlyList<IReadOnlyDictionary<string, object?>>)
    method!.Invoke(api, new object[] { 10 })!;
```

Both paths hand back the same dictionary shape described under [Mission schema](#mission-schema) below.

## `MissionLogApi.Current`

Static property on `VGMissionLog.Api.MissionLogApi`. Returns an `IMissionLogQuery` once the plugin has finished loading, or `null` when:

- The plugin isn't installed.
- BepInEx has loaded the plugin assembly but `Awake` hasn't run yet (rare — consumers that query from their own `Awake` should use the `Chainloader.PluginInfos` guard shown above).
- The plugin is being torn down (`OnDestroy` nulls the facade before Harmony unpatches).

Always null-check.

## `IMissionLogQuery`

All query methods return primitive shapes — either dictionaries keyed by camelCase strings, or `IReadOnlyDictionary<string, int>` for counts. No internal record types leak through the interface, so reflection consumers never need to touch VGMissionLog types.

### Properties

| Property | Type | Meaning |
|---|---|---|
| `SchemaVersion` | `int` | Current sidecar / mission schema version (3). Feature-gate method calls that were added in a later version. |
| `TotalMissionCount` | `int` | Number of mission records currently in the log. |
| `OldestAcceptedGameSeconds` | `double?` | Accept-time (game seconds) of the oldest retained mission. `null` when the log is empty. |
| `NewestAcceptedGameSeconds` | `double?` | Accept-time of the most-recently-accepted mission. `null` when the log is empty. |

### Filter methods

All filters that accept a time window (`sinceGameSeconds`, `untilGameSeconds`) anchor on a mission's **accept time** (`timeline[0].gameSeconds`). Default window is `0` to `double.MaxValue` (entire log). Returns are fresh lists — safe to iterate without defensive copies.

| Method | Purpose |
|---|---|
| `GetMission(missionInstanceId)` | Single mission dict by its instance id, or `null` when not found. |
| `GetActiveMissions()` | Missions that have not yet reached a terminal state (Completed / Failed / Abandoned). |
| `GetAllMissions()` | All missions in the log, no filter. |
| `GetMissionsInSystem(systemId, since?, until?)` | Missions whose `sourceSystemId` matches. Missions without a source system (synthesized archive backstops) are excluded. |
| `GetMissionsByFaction(factionId, since?, until?)` | Missions whose `sourceFaction` matches. |
| `GetMissionsByMissionSubclass(subclass, since?, until?)` | Exact match on `mission.GetType().Name` — e.g. `"BountyMission"`, `"PatrolMission"`, `"IndustryMission"`, `"Mission"`. Case-sensitive. |
| `GetMissionsByOutcome(outcome, since?, until?)` | `outcome` is `"Completed"` / `"Failed"` / `"Abandoned"`. Active missions never match. Invalid string → empty (no throw). |
| `GetMissionsForStoryId(storyId)` | All mission records sharing a `storyId` (authored story arcs may span multiple mission instances). |
| `GetMissionsWithObjective(objectiveType, since?, until?)` | Missions whose `steps[].objectives[].type` contains the given objective type name — e.g. `"KillEnemies"`, `"TravelToPOI"`, `"CollectItemTypes"`, `"Mining"`. Case-sensitive. |
| `GetRecentMissions(count)` | Up to `count` missions, most-recently-accepted first. |

### Proximity

VGMissionLog doesn't walk the galaxy graph itself — you pass a delegate that reports jumps between two systems.

```csharp
int JumpDistance(string fromSystemId, string toSystemId) => /* -1 if unreachable */;
var nearby = api.GetMissionsWithinJumps("sys-zoran", maxJumps: 3, JumpDistance);
```

`GetMissionsWithinJumps(pivotSystemId, maxJumps, jumpDistance, since?, until?)` returns missions whose source system is within `maxJumps` of the pivot. Sourceless and unreachable missions are excluded.

### Aggregates

| Method | Keys | Values |
|---|---|---|
| `CountByMissionSubclass(since?, until?)` | Raw subclass name (`"BountyMission"` etc.) | Mission count |
| `CountByOutcome(since?, until?)` | `"Completed"` / `"Failed"` / `"Abandoned"` | Mission count (active missions not counted) |
| `CountBySystem(since?, until?)` | System id | Mission count (sourceless missions excluded) |
| `CountByFaction(since?, until?)` | Faction id | Mission count (factionless missions excluded) |
| `MostActiveSystemsInRange(pivotSystemId, jumpDistance, maxJumps, topN, since?, until?)` | — | List of `{ systemId, count, jumps }` dicts, sorted by count desc with system-id ordinal tiebreaker. |

## Mission schema

Every mission comes back as `IReadOnlyDictionary<string, object?>`. Keys are camelCase. Null-valued keys are **always present** in the dict (the mapper includes every key); cast with a null-guard or use `ContainsKey` before casting optional fields that may be null.

### Identity fields

Captured once on acceptance and never mutate — vanilla doesn't change a mission after generation.

| Key | Type | Notes |
|---|---|---|
| `storyId` | `string` | Vanilla `Mission.storyId`. **Empty for most missions** — vanilla only populates it for authored story arcs (Tutorial, Puppeteers). Use `missionInstanceId` for correlating across the accept→complete lifecycle when `storyId` is empty. |
| `missionInstanceId` | `string` | Session-local GUID synthesized per `Mission` instance. Stable within a session; does *not* survive save/load — vanilla rebuilds mission objects on load, so a mission accepted in one session and finished in another carries different ids. |
| `missionName` | `string?` | Display name when the mission has one; `null` otherwise. |
| `missionSubclass` | `string` | Raw `mission.GetType().Name`. One of `"Mission"` (parametric missions — salvage, courier, trade, etc.), `"BountyMission"`, `"IndustryMission"`, `"PatrolMission"`, `"StoryMission"`. |
| `missionLevel` | `int` | Currently always `0` — see [Known gaps](#known-gaps). |
| `sourceStationId` | `string?` | Source POI GUID, when the mission has a source station. |
| `sourceStationName` | `string?` | Snapshot at accept time — won't change if vanilla later renames it. |
| `sourceSystemId` | `string?` | System containing the source station. |
| `sourceSystemName` | `string?` | Snapshot at accept time. |
| `sourceSectorId` | `string?` | *Not yet populated — see [Known gaps](#known-gaps).* |
| `sourceSectorName` | `string?` | *Not yet populated.* |
| `sourceFaction` | `string?` | Faction identifier (e.g. `"BountyGuild"`). |
| `targetStationId` | `string?` | *Not yet populated — see [Known gaps](#known-gaps).* |
| `targetStationName` | `string?` | *Not yet populated.* |
| `targetSystemId` | `string?` | *Not yet populated.* |
| `playerLevel` | `int` | Commander level at accept time. `0` in tests / when `GamePlayer.current` is null. |
| `playerShipName` | `string?` | *Not yet populated.* |
| `playerShipLevel` | `int?` | *Not yet populated.* |
| `playerCurrentSystemId` | `string?` | Where the player was when the mission was accepted — may differ from `sourceSystemId`. |

### Steps and objectives

`steps[]` is the mission's step/objective tree captured once at acceptance. Steps are definition-only (no runtime progress counters); each is a dict:

| Key | Type | Meaning |
|---|---|---|
| `description` | `string?` | Vanilla's display description (null when the step has none). |
| `requireAllObjectives` | `bool` | `true` → every objective must complete; `false` → any one does. |
| `hidden` | `bool` | Vanilla flag for branch-stub / guard steps the UI hides. Consumers usually skip these. |
| `objectives` | list | See below. |

Each objective is a dict:

| Key | Type | Meaning |
|---|---|---|
| `type` | `string` | Raw `objective.GetType().Name` — e.g. `"KillEnemies"`, `"TravelToPOI"`, `"Mining"`, `"TradeOffer"`, `"CollectCredits"`, `"CollectItemTypes"`, `"Crafting"`, `"Reputation"`, `"ProtectUnit"`, `"Salvage"`, `"TriggerObjective"`, `"StationsInfected"`, `"SystemsConquered"`, `"ConquestFactionEliminated"`, `"ConquestFleetStrength"`, `"CreditOffer"`, `"DroneTrigger"`, `"Item"`. |
| `fields` | dict? | Best-effort primitive field dump (camelCase keys). For `KillEnemies`: `requiredAmount`, `shipType`, `enemyFaction` (faction identifier). For `TravelToPOI`: `targetPOI`. For `Mining`: `requiredAmount`, `itemType` (identifier), `miningFaction`. What you get depends on what the concrete subclass exposes — anything non-primitive / non-Faction / non-InventoryItemType / non-MapElement is skipped. `null` when extraction yields nothing. |

### Rewards list

`rewards[]` carries one entry per `MissionReward` on the mission. Rewards are extracted at accept time and re-extracted at completion (when vanilla populates the final reward set). Each entry:

| Key | Type | Meaning |
|---|---|---|
| `type` | `string` | Raw `reward.GetType().Name` — one of `"Credits"`, `"Experience"`, `"Reputation"`, `"Item"`, `"Ship"`, `"Crew"`, `"Skilltree"`, `"Skillpoint"`, `"WorkshopCredit"`, `"StoryMission"`, `"MissionFollowUp"`, `"POICoordinates"`, `"UmbralControl"`, `"ConquestStrength"`. |
| `fields` | dict? | Best-effort primitive field dump. `Credits` / `Experience` / `Skillpoint` / `WorkshopCredit` / `UmbralControl` / `ConquestStrength` → `amount`. `Item` → `item` (identifier) + `amount`. `Ship` → `ship` (identifier). `Skilltree` → `treeName`. `StoryMission` → `missionId`. `Reputation` → `amount` + `faction` (identifier). `null` when extraction yields nothing. |

The typed top-level fields from v1 (`rewardsCredits`, `rewardsExperience`, `rewardsReputation`) are **removed** in v3. Read all rewards off `rewards[]` by `type`.

**Identifier rule.** Every resolved reference in `fields` — `enemyFaction`, `miningFaction`, `faction`, `itemType`, `item`, `deliverTo`, `targetPOI`, etc. — is the stable **system identifier** (what vanilla's `Faction.Get(id)` / `InventoryItemType.Get(id)` accept), never the translated `displayName` / `name`. For `InventoryItemType` references on runtime-cloned reward instances (where vanilla's own `identifier` field isn't serialized) we fall back to the Unity object's `name` with the `(Clone)` suffix stripped — still a valid registry key. Consumers can round-trip every id back to the vanilla object.

### Timeline

`timeline[]` is the explicit state-transition log. It grows by one entry on each lifecycle transition:

| Key | Type | Meaning |
|---|---|---|
| `state` | `string` | One of `"Accepted"`, `"Completed"`, `"Failed"`, `"Abandoned"`. |
| `gameSeconds` | `double` | In-game clock at the transition. |
| `realUtc` | `string?` | ISO-8601 wall-clock. Stamped on `Accepted` and terminal entries; `null` on any interior entries that may be added in a future version. |

A mission's timeline always starts with exactly one `Accepted` entry and ends with at most one terminal entry (Completed / Failed / Abandoned). An active mission has no terminal entry.

The helper fields derived from the timeline:

| Key | Type | Meaning |
|---|---|---|
| `acceptedAtGameSeconds` | `double` | Shorthand for `timeline[0].gameSeconds`. |
| `terminalAtGameSeconds` | `double?` | Game-seconds of the terminal entry; `null` when the mission is still active. |
| `outcome` | `string?` | `"Completed"` / `"Failed"` / `"Abandoned"`, or `null` when still active. |
| `isActive` | `bool` | `true` when the mission has no terminal entry yet. |

**Time-window anchoring.** All `since` / `until` filters on query methods use `acceptedAtGameSeconds` as the anchor — not the terminal time. A mission accepted at t=100 that completes at t=200 appears in a `since=50, until=150` window even though it completed outside it.

### Example

```json
{
  "storyId": "anon:a4f2b1c8-...",
  "missionInstanceId": "inst-0001",
  "missionName": "Hunt the Crimson Fang",
  "missionSubclass": "BountyMission",
  "missionLevel": 0,
  "sourceStationId": "poi-zoran-bounty",
  "sourceStationName": "Zoran Bounty Office",
  "sourceSystemId": "sys-zoran",
  "sourceSystemName": "Zoran",
  "sourceSectorId": null,
  "sourceSectorName": null,
  "sourceFaction": "BountyGuild",
  "targetStationId": null,
  "targetStationName": null,
  "targetSystemId": null,
  "playerLevel": 12,
  "playerShipName": null,
  "playerShipLevel": null,
  "playerCurrentSystemId": "sys-zoran",
  "steps": [
    {
      "description": "Eliminate the Crimson Fang raiders",
      "requireAllObjectives": true,
      "hidden": false,
      "objectives": [
        { "type": "KillEnemies", "fields": { "enemyFaction": "CrimsonFang", "requiredAmount": 3 } }
      ]
    }
  ],
  "rewards": [
    { "type": "Credits",    "fields": { "amount": 12000 } },
    { "type": "Experience", "fields": { "amount": 350 } }
  ],
  "timeline": [
    { "state": "Accepted",  "gameSeconds": 12450.0, "realUtc": "2026-04-24T14:12:30Z" },
    { "state": "Completed", "gameSeconds": 13420.0, "realUtc": "2026-04-24T14:25:40Z" }
  ],
  "acceptedAtGameSeconds": 12450.0,
  "terminalAtGameSeconds": 13420.0,
  "outcome": "Completed",
  "isActive": false
}
```

## Sidecar format

If you'd rather read the log without loading VGMissionLog (offline analysis, external tooling), the sidecar JSON uses the same shape. File lives at `<GameDir>/Saves/<saveName>.save.vgmissionlog.json`:

```json
{
  "version": 3,
  "missions": [
    { "storyId": "...", "missionInstanceId": "...", ... },
    ...
  ]
}
```

- Written atomically via tmp+rename, so partial files are never observed.
- Corrupt / unsupported-version files are quarantined to `<saveName>.save.vgmissionlog.corrupt.<UTC>.json` and replaced by an empty log — VGMissionLog never blocks vanilla's load on a bad sidecar.
- `version` bumps on breaking schema changes. Additive changes (new optional fields) stay at the current version.

## Migration

v1 sidecars (schema `"version": 1, "events": [...]`) are **automatically migrated** on first load — no user action required. Each group of events sharing a `missionInstanceId` collapses into a single v3 mission record with a sparse timeline (one `Accepted` entry plus at most one terminal entry). v1 didn't record intermediate transitions and neither does v3, so no timeline information is lost beyond what v1 already omitted. Reward fields (`rewardsCredits`, `rewardsExperience`, `rewardsReputation`) from v1 are folded into the unified `rewards[]` list. The migrated file is written back as v3 immediately.

## Known gaps

These are documented limitations of the current shipping version. Consumers should treat the affected keys as always-absent for now.

- **`missionLevel` is always `0`.** Vanilla's `Mission.level` getter chains through `GamePlayer.current`, which makes it unsafe to read outside the main thread. A safer accessor is on the roadmap.
- **Sector and target-station/system fields are never populated.** Vanilla's accessor graph is deeper than what's currently scouted. Fields are reserved in the schema; additive future.
- **Player ship fields are never populated.** Same reason.
- **`missionInstanceId` is session-local.** Resets across save/load because vanilla rebuilds mission instances on load. Accept→Complete in the same session works; across sessions you need `storyId` (if set) or your own joining logic.
- **No `Offered` or `ObjectiveProgressed` events.** Accepted is load-bearing; Offered varies too much by source (board, bar, broker) to hook reliably at this stage. `steps[]` captured on accept lets consumers reconstruct which objectives a mission carried without needing an `ObjectiveProgressed` stream.

## Retention

The log defaults to a soft cap of **2000 missions per save**, with FIFO eviction once exceeded — oldest-accepted missions drop off first. Consumers should not assume the full playtime is always available; use `OldestAcceptedGameSeconds` if you need to know how far back the log reaches.

The cap is configurable via `Logging.MaxMissions` in `vgmissionlog.cfg`. Setting it to **`0` disables the cap entirely** — the log then retains every mission for the save's lifetime, at the cost of an unbounded sidecar size. A user with 50 000 captured missions would sit around 25 MB on disk (rough estimate at ~500 bytes per record).

## Stability

- **Method signatures on `IMissionLogQuery`** are part of the public API contract. Breaking changes require a major-version bump and release notes.
- **New methods are additive** — adding a method doesn't bump major. Gate on `SchemaVersion` if you want to call a newer method conditionally.
- **Mission dictionary keys** follow the same rules: additions are non-breaking, renames / removals are breaking.
- **The sidecar `version` field** tracks only on-disk format changes. An additive API change doesn't necessarily bump it.
