# 02 — Requirements

The "must capture / must answer / must persist" contract. No design decisions yet — those live in `03-architecture.md`.

## R1 — Event capture (what must be recorded)

VGMissionLog must record **every mission lifecycle transition** as a discrete `ActivityEvent`. A mission's full history is reconstructed by filtering events by its identifier.

### R1.1 — Required event types

| Event type | Fires when |
|---|---|
| `Offered` | A mission is added to a board / bar / broker the player can see |
| `Accepted` | Player accepted the mission (vanilla's `AddMissionWithLog`) |
| `Completed` | Terminal success (vanilla's `CompleteMission` + subsequent `ArchiveMission`) |
| `Failed` | Terminal failure by condition violation (vanilla's `Mission.MissionFailed`) |
| `Abandoned` | Player-initiated drop (vanilla's `RemoveMission(..., completed:false)`) |
| `ObjectiveProgressed` | A mission step / objective advanced (optional — see R1.4) |

`Offered` is best-effort — vanilla's offer path varies by source (mission board regenerates parametrically; bar salesmen materialize on bar refresh; third-party mods may dispatch asynchronously). The implementor should capture whatever is reliably observable and flag any gaps.

### R1.2 — Required per-event fields

Every `ActivityEvent` must carry the following. Fields with `*` may be snapshot-style nullable when vanilla provides no clean source at the moment of capture.

| Field | Type | Purpose |
|---|---|---|
| `eventId` | string (GUID) | Stable key for dedup / joins |
| `eventType` | enum | One of R1.1 above |
| `gameSeconds` | double | In-game clock timestamp (for age queries) |
| `realUtc` | ISO-8601 string | Wall-clock timestamp (debugging) |
| `storyId` | string | Mission's vanilla storyId when present; synthesized when absent |
| `missionName`* | string | Display name at event time |
| `missionType` | enum | Classified category: `Bounty` / `Patrol` / `Industry` / `Story` / `Generic` / `ThirdParty(prefix)` |
| `missionSubclass` | string | Raw vanilla type name (`BountyMission`, `PatrolMission`, …) for debugging / future re-classification |
| `missionLevel` | int | Level at event time |
| `archetype` | enum? | Inferred when possible (`Combat`/`Gather`/`Salvage`/`Deliver`/`Escort`/`Other`) — null when unclassifiable |
| `outcome` | enum? | For terminal events: `Completed` / `Failed` / `Abandoned`. Null for non-terminal |
| `sourceStationId` | string? | Station GUID where the mission originated (broker station, mission-board station, etc.) |
| `sourceStationName`* | string? | Snapshot |
| `sourceSystemId` | string? | System GUID containing the source station |
| `sourceSystemName`* | string? | Snapshot |
| `sourceSectorId` | string? | Sector GUID containing the source system |
| `sourceSectorName`* | string? | Snapshot |
| `sourceFaction` | string? | Faction identifier (e.g. `BountyGuild`, `TradingGuild`) |
| `targetStationId` | string? | For deliver / haul missions |
| `targetStationName`* | string? | Snapshot |
| `targetSystemId` | string? | For missions whose payoff fires elsewhere |
| `facilityOrigin` | enum? | `Bar` / `MissionBoard` / `BountyBoard` / `PoliceBoard` / `IndustryBoard` / `Other` — if discernible |
| `rewardsCredits`* | long? | Credits paid on completion (null for non-terminal / failed) |
| `rewardsExperience`* | long? | XP paid on completion |
| `rewardsReputation`* | list of `{faction, amount}` | Rep changes applied |
| `playerLevel` | int | Commander level at event time |
| `playerShipName`* | string? | Ship the player was flying when the event fired |
| `playerShipLevel`* | int? | That ship's level |
| `playerCurrentSystemId`* | string? | Where the player was when the event fired (may differ from sourceSystemId — e.g. Accept at the bar vs Complete at the target POI) |

### R1.3 — Name snapshots

Display names (`missionName`, `sourceStationName`, etc.) must be captured at the moment of the event. Vanilla may rename / regenerate / destroy stations later; the log must not rely on live vanilla state for rendering historical events.

### R1.4 — Objective progression (optional, deferred)

`ObjectiveProgressed` events (partial progress inside a mission step) are nice-to-have. Ship MVP without them. If added later, follow the same event-shape-extension pattern so historical data stays readable.

## R2 — Query API (what consumers must be able to ask)

The public API must answer these queries in ≤ 5ms on logs up to 2000 events. Signatures are illustrative — implementor chooses the exact shape.

### R2.1 — Raw filters

- `GetEventsInSystem(systemGuid, sinceGameSeconds=0, untilGameSeconds=now)`
- `GetEventsInSector(sectorGuid, sinceGameSeconds, untilGameSeconds)`
- `GetEventsByFaction(factionId, sinceGameSeconds, untilGameSeconds)`
- `GetEventsByMissionType(missionType, sinceGameSeconds, untilGameSeconds)`
- `GetEventsByOutcome(outcome, sinceGameSeconds, untilGameSeconds)`
- `GetEventsForStoryId(storyId)` — full per-mission timeline
- `GetRecentEvents(count, filter=null)` — most-recent-first

### R2.2 — Proximity filters (distance from a pivot system)

- `GetEventsWithinJumps(pivotSystemGuid, maxJumps, sinceGameSeconds)` — requires a graph supplier (jump-distance function) passed by the consumer, so VGMissionLog stays graph-agnostic
- `GetEventsSortedByJumps(pivotSystemGuid, graphSupplier, filter)` — for reach-formula-style consumers

### R2.3 — Aggregates

- `CountByType(sinceGameSeconds, untilGameSeconds)` → `{bounty: N, patrol: M, …}`
- `CountByOutcome(sinceGameSeconds, untilGameSeconds)` → `{completed: N, failed: M, abandoned: K}`
- `CountBySystem(sinceGameSeconds, untilGameSeconds)` → `{systemGuid: count, …}`
- `CountByFaction(sinceGameSeconds, untilGameSeconds)` → `{factionId: count, …}`
- `MostActiveSystemsInRange(pivotSystemGuid, graphSupplier, maxJumps, topN)`

### R2.4 — Property access

- `TotalEventCount` — cheap size probe
- `OldestEventGameSeconds` / `NewestEventGameSeconds` — for time-window sanity
- `SchemaVersion` — so consumers can feature-gate on log capability

## R3 — Persistence contract

### R3.1 — Per-save sidecar

- One log per vanilla save slot.
- File name: `<saveName>.save.vgmissionlog.json`.
- Same directory as vanilla's saves (`{persistentDataPath}/Saves/` on PC).

### R3.2 — Save/load lifecycle

- **On vanilla save-write** — flush the in-memory log to the sidecar. Postfix hook after vanilla's own save succeeds; never block vanilla's write path.
- **On vanilla save-load** — read the paired sidecar and replace the in-memory log wholesale. Missing / corrupt / unreadable → empty in-memory log + warn-level log line.
- **On ApplicationQuit** — best-effort flush to the last-known save path as a safety net.
- **On save-delete** (best-effort) — orphan sidecar sweep at plugin load.

### R3.3 — Atomic writes

Writes must be crash-safe: write to a `.tmp` file first, then rename. No half-written sidecars.

### R3.4 — Corruption handling

A corrupt sidecar must not poison vanilla's load. Read failures quarantine the bad file (`.corrupt.<timestamp>.json`) and start with an empty log.

### R3.5 — Versioning

- Top-level `version: int` field, bumped on every breaking schema change.
- **Additive changes (new fields on events, new event types)** — readable without version bump when fields are nullable / opt-in.
- **Breaking changes (field removal, semantic shifts)** — bump version; old versions quarantine on load OR upgrade-on-read (additive in-memory restamp when the prior version is one back).
- The initial ship version is `1`.

### R3.6 — Capacity bound

- Soft cap at **2000 events** per save; FIFO eviction once exceeded.
- Cap is a constant, not config-exposed at MVP — keeps sidecars under ~1 MB in realistic play.
- Implementor should emit a one-time warning when the cap first kicks in so the player/consumer knows eviction started.

### R3.7 — Security

- Sidecar content is user-authored trust-surface (may travel via cloud-sync, shared saves). If Newtonsoft's `TypeNameHandling.Auto` is used (not recommended for this mod — primitive-typed records only), it MUST be paired with a locked-down `ISerializationBinder`.
- VGMissionLog should prefer plain primitive records → no `$type` discriminators → no binder complexity.

## R4 — Plugin surface

### R4.1 — BepInEx plugin attributes

- GUID: `vgmissionlog` (lowercase, stable forever).
- Display name: `Vanguard Galaxy Mission Log`.
- Dependencies: `[BepInProcess("VanguardGalaxy.exe")]`. No hard deps on other mods.

### R4.2 — Public API for consumers

- Static facade class with a stable name and namespace (implementor to pick — candidate: `VGMissionLog.Api.MissionLogApi`).
- Public accessor: `MissionLogApi.Current` returns the live log query interface, or `null` when the plugin isn't loaded (consumers reflection-probe for this).
- API surface MUST be stable across minor-version bumps. Breaking API changes require a major-version bump and a documented migration.

### R4.3 — Reflection-friendly soft-dep shape

Consumers use reflection to locate `MissionLogApi` by type name (e.g. `Type.GetType("VGMissionLog.Api.MissionLogApi, VGMissionLog")`). The query methods must accept and return only primitive types + `List<T>` / `Dictionary<T,V>` of primitives — **no consumer-side type dependency** on VGMissionLog's event classes. Consumers get back `IReadOnlyList<IReadOnlyDictionary<string, object?>>` or similar neutral shapes.

Alternatively: VGMissionLog publishes a tiny `VGMissionLog.Contracts` assembly that consumers *can* optionally reference for typed shapes, but the reflection path must remain viable for soft-dep consumers.

### R4.4 — Logging / diagnostics

- Per-event debug log line at BepInEx Debug level (opt-in via config).
- Summary info lines at Info level: "Loaded N events from sidecar", "Flushed N events to sidecar", "Evicted K oldest events".
- No Error-level spam for expected states (missing sidecar, corrupt sidecar → warn, not error).

### R4.5 — Config (minimal)

- `Logging.Verbose` (bool, default false) — toggles per-event debug output.
- `Persistence.MaxEvents` (int, default 2000) — capacity cap override.
- Nothing else at MVP. Future: facility-specific opt-outs if needed.

## R5 — Compatibility + non-interference

### R5.1 — Harmony patches must be postfixes only

No Prefix patches except where strictly required to read pre-mutation state. Postfix-only is the invariant — guarantees VGMissionLog never changes behavior.

### R5.2 — Exception isolation

Any exception in a Harmony postfix must be caught and logged at Warning level. Vanilla's execution path must never be affected by VGMissionLog's internal state. If logging an event fails, the event is dropped — vanilla keeps running.

### R5.3 — Third-party-mod mission support

Mods other than vanilla may author missions. Those missions flow through vanilla's `AddMissionWithLog` like any other, so they're captured automatically — the raw `mission.GetType().Name` and the game-provided `storyId` carry whatever signal the consumer needs to attribute them. VGMissionLog does not classify mod authorship; consumers read the raw values and bucket as they see fit.

### R5.4 — Broken or missing peer mods

If a consumer of VGMissionLog is not installed, nothing changes — the log keeps running in write-only mode. Consumers are late-binding; producers are eager.

## R6 — Testing

### R6.1 — Unit tests

- Event classification (each vanilla mission subclass routes to the right `missionType`).
- Query API correctness (system filter, faction filter, proximity filter with injected graph, aggregates).
- Sidecar roundtrip (write → read → compare equality).
- Corruption handling (malformed JSON, unsupported version, missing file).
- Capacity eviction (oldest entries drop when cap exceeded).
- Timestamp / clock substitution for deterministic test timestamps.

### R6.2 — Integration / smoke

- Plugin-load smoke check: Harmony patch count increments on plugin attach (catches refactors where a method name changes and the patch silently no-ops).
- Manual E2E: accept → complete a bounty mission, confirm one event appears per lifecycle step; same for abandon / fail.

### R6.3 — Performance

- A synthetic test with 2000 events verifying all R2 queries complete in ≤ 5ms (relaxed budget — this is observational and non-critical).
