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
        foreach (var evt in api.GetRecentEvents(10))
            Logger.LogInfo($"{evt["type"]} {evt["missionSubclass"]}");
    }
}
```

### Reflection fallback

For scripting-style mods that can't add a compile-time reference:

```csharp
var facade = Type.GetType("VGMissionLog.Api.MissionLogApi, VGMissionLog");
var api    = facade?.GetProperty("Current")?.GetValue(null);
if (api is null) return;  // plugin not installed

var method = api.GetType().GetMethod("GetRecentEvents", new[] { typeof(int) });
var events = (IReadOnlyList<IReadOnlyDictionary<string, object?>>)
    method!.Invoke(api, new object[] { 10 })!;
```

Both paths hand back the same dictionary shape described under [Event schema](#event-schema) below.

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
| `SchemaVersion` | `int` | Current sidecar / event schema version. Feature-gate method calls that were added in a later version. |
| `TotalEventCount` | `int` | Size of the in-memory log. |
| `OldestEventGameSeconds` | `double?` | In-game seconds of the oldest retained event. `null` when the log is empty. |
| `NewestEventGameSeconds` | `double?` | Same for the most-recent event. |

### Filter methods

All filters support an optional time window on `gameSeconds` (default: entire log). Events outside the window are excluded. Returns are fresh lists — safe to iterate without defensive copies.

| Method | Purpose |
|---|---|
| `GetEventsInSystem(systemId, since?, until?)` | Events whose `sourceSystemId` matches. Events without a source system (synthesized archive backstops) don't match. |
| `GetEventsByFaction(factionId, since?, until?)` | Events whose `sourceFaction` matches. |
| `GetEventsByMissionSubclass(subclass, since?, until?)` | Exact match on raw `mission.GetType().Name` — e.g. `"BountyMission"`, `"PatrolMission"`, `"IndustryMission"`, `"Mission"`. Case-sensitive. |
| `GetEventsByOutcome(outcome, since?, until?)` | `outcome` is `"Completed"` / `"Failed"` / `"Abandoned"`. Non-terminal events never match. Invalid string → empty (no throw). |
| `GetEventsForStoryId(storyId)` | Full per-mission timeline in insertion order (typically Accepted → Completed/Failed/Abandoned). |
| `GetRecentEvents(count)` | Up to `count` events, most-recent first. |

### Proximity

VGMissionLog doesn't walk the galaxy graph itself — you pass a delegate that reports jumps between two systems.

```csharp
int JumpDistance(string fromSystemId, string toSystemId) => /* -1 if unreachable */;
var nearby = api.GetEventsWithinJumps("sys-zoran", maxJumps: 3, JumpDistance);
```

`GetEventsWithinJumps(pivotSystemId, maxJumps, jumpDistance, since?, until?)` returns events whose source system is within `maxJumps` of the pivot. Sourceless and unreachable events are excluded.

### Aggregates

| Method | Keys | Values |
|---|---|---|
| `CountByMissionSubclass(since?, until?)` | Raw subclass name (`"BountyMission"` etc.) | Event count |
| `CountByOutcome(since?, until?)` | `"Completed"` / `"Failed"` / `"Abandoned"` | Event count (only terminal events) |
| `CountBySystem(since?, until?)` | System id | Event count (sourceless events excluded) |
| `CountByFaction(since?, until?)` | Faction id | Event count (factionless events excluded) |
| `MostActiveSystemsInRange(pivotSystemId, jumpDistance, maxJumps, topN, since?, until?)` | — | List of `{ systemId, count, jumps }` dicts, sorted by count desc with system-id ordinal tiebreaker. |

## Event schema

Every event comes back as `IReadOnlyDictionary<string, object?>`. Keys are camelCase. Null-valued keys are **omitted**, not stored as `null`, so always `ContainsKey` before casting optional fields.

### Always present

| Key | Type | Notes |
|---|---|---|
| `eventId` | `string` | Fresh GUID per event. Stable across reloads. |
| `type` | `string` | One of `"Accepted"`, `"Completed"`, `"Failed"`, `"Abandoned"`. |
| `gameSeconds` | `double` | In-game clock at capture. |
| `realUtc` | `string` | ISO-8601 wall-clock at capture. |
| `storyId` | `string` | Vanilla `Mission.storyId`. **Empty for most missions** — vanilla only populates it for authored story arcs (Tutorial, Puppeteers). Use `missionInstanceId` for correlating events otherwise. |
| `missionInstanceId` | `string` | Session-local GUID synthesized per `Mission` instance. Every lifecycle event from the same mission shares this id, so you can link `Accepted` → `Completed`/`Failed`/`Abandoned` even when `storyId` is empty. **Caveat:** the id does *not* survive save/load — vanilla rebuilds mission objects on load, so a mission accepted in one session and finished in another will have different ids on each event. |
| `missionSubclass` | `string` | Raw `mission.GetType().Name`. One of `"Mission"` (parametric missions from `MissionGenerator` — salvage, courier, trade, etc.), `"BountyMission"`, `"IndustryMission"`, `"PatrolMission"`, `"StoryMission"`. The vast majority of gameplay spawns base `"Mission"`; only typed subclasses opt in. |
| `missionLevel` | `int` | Currently always `0` — see [Known gaps](#known-gaps). |
| `playerLevel` | `int` | Commander level at capture. `0` in tests / when `GamePlayer.current` is null. |

### Optional (present only when non-null)

| Key | Type | Present on |
|---|---|---|
| `missionName` | `string` | When the mission has a display name. |
| `outcome` | `string` | Terminal events only. `"Completed"` / `"Failed"` / `"Abandoned"`. |
| `sourceStationId` | `string` | Source POI GUID, when the mission has a source station. |
| `sourceStationName` | `string` | Snapshot at capture time — won't change if vanilla later renames it. |
| `sourceSystemId` | `string` | System containing the source station. |
| `sourceSystemName` | `string` | Snapshot. |
| `sourceSectorId` | `string` | *Not yet populated — see [Known gaps](#known-gaps).* |
| `sourceSectorName` | `string` | *Not yet populated.* |
| `sourceFaction` | `string` | Faction identifier (e.g. `"BountyGuild"`). |
| `targetStationId` | `string` | *Not yet populated — see [Known gaps](#known-gaps).* |
| `targetStationName` | `string` | *Not yet populated.* |
| `targetSystemId` | `string` | *Not yet populated.* |
| `rewardsCredits` | `long` | Completed events only. Summed across all Credits entries. |
| `rewardsExperience` | `long` | Completed events only. Summed across all Experience entries. Most generator missions don't grant XP — absence is normal. |
| `rewardsReputation` | `IReadOnlyList<IReadOnlyDictionary<string, object?>>` | Completed events. Each entry has `faction` (string) and `amount` (int). |
| `rewards` | `IReadOnlyList<IReadOnlyDictionary<string, object?>>` | Completed events. Unified list covering all 14 vanilla reward subtypes (Credits, Experience, Reputation, Item, Ship, Crew, Skilltree, Skillpoint, WorkshopCredit, StoryMission, MissionFollowUp, POICoordinates, UmbralControl, ConquestStrength). See [Rewards list](#rewards-list). |
| `steps` | `IReadOnlyList<IReadOnlyDictionary<string, object?>>` | Mission's step/objective tree at capture time. See [Steps and objectives](#steps-and-objectives). |
| `playerShipName` | `string` | *Not yet populated.* |
| `playerShipLevel` | `int` | *Not yet populated.* |
| `playerCurrentSystemId` | `string` | Where the player was when the event fired — may differ from `sourceSystemId` (e.g. Complete at the target POI). |

### Steps and objectives

`steps[]` is the mission's step tree captured verbatim at the event's transition. Each step is a dict:

| Key | Type | Meaning |
|---|---|---|
| `description` | `string` | Vanilla's display description (omitted when the step has none). |
| `isComplete` | `bool` | `MissionStep.isComplete` — true when all required objectives pass. |
| `requireAllObjectives` | `bool` | `true` → every objective must complete; `false` → any one does. |
| `hidden` | `bool` | Vanilla flag for branch-stub / guard steps the UI hides. Consumers usually skip these. |
| `objectives` | list | See below. |

Each objective is a dict:

| Key | Type | Meaning |
|---|---|---|
| `type` | `string` | Raw `objective.GetType().Name` — `"KillEnemies"`, `"TravelToPOI"`, `"Mining"`, `"TradeOffer"`, `"CollectCredits"`, `"CollectItemTypes"`, `"Crafting"`, `"Reputation"`, `"ProtectUnit"`, `"Salvage"`, `"TriggerObjective"`, `"StationsInfected"`, `"SystemsConquered"`, `"ConquestFactionEliminated"`, `"ConquestFleetStrength"`, `"CreditOffer"`, `"DroneTrigger"`, `"Item"`. |
| `isComplete` | `bool` | `objective.IsComplete()`. |
| `statusText` | `string` | Vanilla's translated progress string (e.g. `"Kill 3/5 Pirates"`). Optional — omitted when the localizer isn't ready or the objective throws on read. |
| `fields` | dict | Best-effort primitive field dump (camelCase keys). For `KillEnemies`: `requiredAmount`, `currentAmount`, `shipType`, `enemyFaction` (resolved to faction identifier). For `TravelToPOI`: `targetPOI`. For `Mining`: `requiredAmount`, `currentAmount`, `itemType` (identifier), `miningFaction`. Etc. What you get depends on what the concrete subclass exposes — anything non-primitive / non-Faction / non-InventoryItemType / non-MapElement is skipped. |

### Rewards list

**Identifier rule.** Every resolved reference in `fields` — `enemyFaction`, `miningFaction`, `faction`, `itemType`, `item`, `deliverTo`, `targetPOI`, etc. — is the stable **system identifier** (what vanilla's `Faction.Get(id)` / `InventoryItemType.Get(id)` accept), never the translated `displayName` / `name`. For `InventoryItemType` references on runtime-cloned reward instances (where vanilla's own `identifier` field isn't serialized) we fall back to the Unity object's `name` with the `(Clone)` suffix stripped — still a valid registry key. Consumers can round-trip every id back to the vanilla object.

`rewards[]` carries one entry per `MissionReward` on the mission. Each entry:

| Key | Type | Meaning |
|---|---|---|
| `type` | `string` | Raw `reward.GetType().Name` — `"Credits"`, `"Experience"`, `"Reputation"`, `"Item"`, `"Ship"`, `"Crew"`, `"Skilltree"`, `"Skillpoint"`, `"WorkshopCredit"`, `"StoryMission"`, `"MissionFollowUp"`, `"POICoordinates"`, `"UmbralControl"`, `"ConquestStrength"`. |
| `fields` | dict | Best-effort primitive field dump. `Credits`/`Experience`/`Skillpoint`/`WorkshopCredit`/`UmbralControl`/`ConquestStrength` → `amount`. `Item` → `item` (identifier) + `amount`. `Ship` → `ship` (identifier). `Crew` → whatever `CrewMemberData` exposes primitively. `Skilltree` → `treeName`. `StoryMission` → `missionId`. `Reputation` → `amount` + `faction` (identifier). Optional — omitted when extraction yields nothing. |

The typed top-level `rewardsCredits` / `rewardsExperience` / `rewardsReputation` keys are kept for convenience (numeric sums, common case) but `rewards[]` is the authoritative source.

### Example

```json
{
  "eventId": "4f1b…",
  "type": "Completed",
  "gameSeconds": 18347.2,
  "realUtc": "2026-04-23T20:31:12.0000000Z",
  "storyId": "",
  "missionInstanceId": "9c2e…",
  "missionSubclass": "BountyMission",
  "missionLevel": 0,
  "playerLevel": 12,
  "missionName": "Pirate Hunt",
  "outcome": "Completed",
  "sourceStationId": "…",
  "sourceStationName": "Zoran Bounty Office",
  "sourceSystemId": "…",
  "sourceSystemName": "Zoran",
  "sourceFaction": "BountyGuild",
  "rewardsCredits": 1500,
  "rewardsExperience": 200,
  "rewardsReputation": [{ "faction": "BountyGuild", "amount": 5 }],
  "rewards": [
    { "type": "Credits",    "fields": { "amount": 1500 } },
    { "type": "Experience", "fields": { "amount": 200 } },
    { "type": "Reputation", "fields": { "amount": 5, "faction": "BountyGuild" } }
  ],
  "steps": [
    {
      "description": "Destroy the pirate fleet",
      "isComplete": true,
      "requireAllObjectives": true,
      "hidden": false,
      "objectives": [
        {
          "type": "KillEnemies",
          "isComplete": true,
          "statusText": "Pirates destroyed: 5/5",
          "fields": { "requiredAmount": 5, "currentAmount": 5, "enemyFaction": "Pirates", "shipType": "Fighter" }
        }
      ]
    }
  ]
}
```

## Sidecar format

If you'd rather read the log without loading VGMissionLog (offline analysis, external tooling), the sidecar JSON uses the same shape. File lives at `<GameDir>/Saves/<saveName>.save.vgmissionlog.json`:

```json
{
  "version": 1,
  "events": [
    { "eventId": "…", "type": "Accepted", ... },
    ...
  ]
}
```

- Written atomically via tmp+rename, so partial files are never observed.
- Corrupt / unsupported-version files are quarantined to `<saveName>.save.vgmissionlog.corrupt.<UTC>.json` and replaced by an empty log — VGMissionLog never blocks vanilla's load on a bad sidecar.
- `version` bumps on breaking schema changes. Additive changes (new optional fields, new lifecycle events) stay at the current version.

## Known gaps

These are documented limitations of the current shipping version. Consumers should treat the affected keys as always-absent for now.

- **`missionLevel` is always `0`.** Vanilla's `Mission.level` getter chains through `GamePlayer.current`, which makes it unsafe to read outside the main thread. A safer accessor is on the roadmap.
- **Sector and target-station/system fields are never populated.** Vanilla's accessor graph is deeper than what's currently scouted. Fields are reserved in the schema; additive future.
- **Player ship fields are never populated.** Same reason.
- **Reputation rewards carry only the faction identifier.** Display metadata (name, colour) isn't persisted; resolve from your own faction data if you need it.
- **`missionInstanceId` is session-local.** Resets across save/load because vanilla rebuilds mission instances on load. Accept→Complete in the same session works; across sessions you need `storyId` (if set) or your own joining logic.
- **No `Offered` or `ObjectiveProgressed` events.** Accepted is load-bearing; Offered varies too much by source (board, bar, broker) to hook reliably at this stage. `steps[]` captured on each terminal event lets consumers reconstruct objective progress without needing a `ObjectiveProgressed` stream.

## Retention

The log defaults to a soft cap of **2000 events per save**, with FIFO eviction once exceeded — oldest events drop off first. Consumers should not assume the full playtime is always available; use `OldestEventGameSeconds` if you need to know how far back the log reaches.

The cap is configurable via `Persistence.MaxEvents` in `vgmissionlog.cfg`. Setting it to **`0` disables the cap entirely** — the log then retains every event for the save's lifetime, at the cost of an unbounded sidecar size (~500 bytes per event). A user with 50 000 captured events would sit around 25 MB on disk.

## Stability

- **Method signatures on `IMissionLogQuery`** are part of the public API contract. Breaking changes require a major-version bump and release notes.
- **New methods are additive** — adding a method doesn't bump major. Gate on `SchemaVersion` if you want to call a newer method conditionally.
- **Event dictionary keys** follow the same rules: additions are non-breaking, renames / removals are breaking.
- **The sidecar `version` field** tracks only on-disk format changes. An additive API change doesn't necessarily bump it.
