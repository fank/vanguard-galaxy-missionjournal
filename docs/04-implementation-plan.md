# 04 — Implementation Plan

A top-to-bottom ordered task list. Each task is atomic (build-green + committable on its own), with clear done-criteria and dependencies. An implementation agent can execute sequentially.

**Conventions**: Build with `DOTNET_ROLL_FORWARD=LatestMajor dotnet build VGMissionLog.sln`. Run tests the same way. Harmony patches use postfix-only pattern with exception-swallow + null-guard. Commit in logical chunks with Conventional Commits messages (`feat:`, `fix:`, `docs:`, `test:`).

The decomp lives at `/tmp/vg-decomp/Assembly-CSharp.decompiled.cs` (single huge file) — grep it for method signatures before patching.

---

## Phase 0 — Bootstrap

### ML-T0a: Project scaffolding
Create `VGMissionLog.sln`, `VGMissionLog/VGMissionLog.csproj`, `VGMissionLog.Tests/VGMissionLog.Tests.csproj`. Standard BepInEx 5 netstandard2.1 plugin shape; xUnit test project on net8.0 with `DOTNET_ROLL_FORWARD=LatestMajor`. Wire `InternalsVisibleTo` for tests.
**Done when**: `dotnet build` succeeds with an empty project.

### ML-T0b: Makefile
Author a Makefile with variables (`DLL`, `GAME_DIR`, `PLUGIN_DIR`) and targets: `build` / `test` / `deploy` / `link-asm` / `clean`.
**Done when**: `make build` produces `bin/Debug/netstandard2.1/VGMissionLog.dll`.

### ML-T0c: Assembly-CSharp symlink
Add `link-asm` target pointing at a sibling VGTTS checkout's publicized stub. Run it.
**Done when**: `VGMissionLog/lib/Assembly-CSharp.dll` exists as a symlink and `dotnet build` still succeeds.

### ML-T0d: Empty Plugin.cs
`[BepInPlugin("vgmissionlog", "Vanguard Galaxy Mission Log", "0.1.0")]` + `[BepInProcess("VanguardGalaxy.exe")]`. Awake logs "plugin loaded". No hooks yet.
**Done when**: DLL loads in-game, BepInEx log shows the "loaded" line.

---

## Phase 1 — Core data model + in-memory log

### ML-T1a: Enums + record types
Create `Logging/ActivityEventType.cs`, `MissionType.cs` (allow a `ThirdParty(prefix)` variant — use a sealed record hierarchy or a string-valued "kind" field), `Outcome.cs`, `ActivityArchetype.cs`, `FacilityOrigin.cs`, `ActivityEvent.cs`, `RepReward.cs`. All primitive fields; no `TypeNameHandling.Auto` surface.

### ML-T1b: ActivityLog
Create `Logging/ActivityLog.cs` with `Append`, `LoadFrom`, `AllEvents`, `TotalEventCount`. FIFO eviction at `MaxEvents`. Rebuild indexes (by storyId, by system, by faction) on load.
**Tests**: append → retrieve round-trip, eviction at cap, index rebuild after LoadFrom.

### ML-T1c: Basic queries
Add R2.1 methods: `GetEventsInSystem`, `GetEventsByFaction`, `GetEventsByMissionType`, `GetEventsByOutcome`, `GetEventsForStoryId`, `GetRecentEvents`. All take `sinceGameSeconds` + `untilGameSeconds` filter.
**Tests**: one test per filter, time-window + exclusion cases.

### ML-T1d: Proximity queries
Add R2.2 `GetEventsWithinJumps(pivotGuid, maxJumps, jumpDistance, since, until)`. Caller supplies `Func<string, string, int>` — VGMissionLog never walks a graph itself.
**Tests**: in-memory graph closure, multiple distance cases, unreachable → excluded.

### ML-T1e: Aggregate queries
Add R2.3 `CountByType`, `CountByOutcome`, `CountBySystem`, `CountByFaction`, `MostActiveSystemsInRange`.
**Tests**: deterministic count checks, empty-log behavior.

### ML-T1f: IClock + timestamp injection
Create `IClock` / `GameClock` (production) / test-fake. All `ActivityEvent` emission goes through the clock so tests can assert deterministic timestamps.
**Done when**: all prior tests pass with an injected clock; no hardcoded `DateTime.UtcNow` inside the log.

---

## Phase 2 — Classification

### ML-T2a: MissionClassifier
Create `Classification/MissionClassifier.cs` — pattern-match on `Mission` subclass → `MissionType`. Fall-through to `Story` or `Generic`.
**Tests**: one per known subclass (stubs of vanilla `BountyMission` etc. using the publicized stub), plus `ThirdParty` via a `_llm_`-infixed storyId.

### ML-T2b: StoryIdPrefixMap
Tiny helper extracting a namespace-like prefix from a storyId by splitting on a configurable infix. Third-party mods register their own infix; a built-in default covers common conventions.
**Tests**: extraction, empty / malformed storyIds.

### ML-T2c: Archetype inference
Walk the `Mission`'s step / objective list and classify:
- `KillEnemies` → Combat
- `CollectItemTypes(Ore|RefinedProduct)` → Gather
- `CollectItemTypes(Salvage|Junk)` → Salvage
- `TravelToPOI` alone → Deliver
- `ProtectUnit` → Escort
- Otherwise null

**Tests**: one per case. Use the publicized `Mission` stub.

### ML-T2d: FacilityOrigin inference
Best-effort — infer from mission type: `BountyMission` → `BountyBoard`, `PatrolMission` → `PoliceBoard`, `IndustryMission` → `IndustryBoard`. Unknown → null. Bar-origin missions rely on the offer hook in Phase 4.

---

## Phase 3 — Persistence

### ML-T3a: LogSchema
`Persistence/LogSchema.cs` — top-level record `(version: int, events: ActivityEvent[])`, `CurrentVersion = 1`, serializer settings (standard Newtonsoft, camelCase, no `TypeNameHandling.Auto`).

### ML-T3b: LogIO
`Persistence/LogIO.cs`:
- `Read(path)` → `{Loaded, MissingFile, Corrupted, UnsupportedVersion}` + optional quarantine path
- `Write(path, schema)` — tmp + rename
- Corruption / unsupported version → quarantine to `<path>.corrupt.<timestamp>.json`
**Tests**: missing file, corrupt JSON, unsupported version, roundtrip, atomic-no-temp-leak.

### ML-T3c: LogPathResolver
`Persistence/LogPathResolver.cs::From("<save>.save") → "<save>.save.vgmissionlog.json"`. Handles `.save` vs non-`.save` inputs.
**Tests**: positive + edge cases.

### ML-T3d: DeadSidecarSweeper
Scan `SaveGame.SavesPath` for `*.save.vgmissionlog.json` and delete ones whose paired `*.save` is missing. Runs once at plugin load.
**Tests**: temp-dir fixture with orphaned + paired files.

---

## Phase 4 — Harmony hooks

### ML-T4a: Scout vanilla save lifecycle
Confirm method names for `SaveGame.Store` and `SaveGameFile.LoadSaveGame` are still current. Grep the decomp.
**Done when**: a scout memo is added to the PR describing exact signatures.

### ML-T4b: SaveWritePatch + SaveLoadPatch
Postfix on `Store`: flush log to sidecar. Prefix on `LoadSaveGame`: read sidecar, replace in-memory log. `Application.quitting` safety-net flush.
**Done when**: save → quit → load → log survives.

### ML-T4c: MissionAcceptPatch
Harmony postfix on `GamePlayer.AddMissionWithLog(Mission)`. Emit `Accepted` event with full snapshot.
**Tests**: patch-attaches-smoke (compile-time), manual E2E (accept a vanilla mission, verify log entry).

### ML-T4d: MissionCompletePatch
Postfix on `GamePlayer.CompleteMission(Mission, bool)`. Capture rewards — **read them from the Mission object's reward list BEFORE vanilla deallocates it** (check decomp for timing). Emit `Completed`.
**Done when**: a completed mission's credits / XP / rep land in the event.

### ML-T4e: MissionFailPatch
Postfix on `Mission.MissionFailed(string)`. Emit `Failed`.

### ML-T4f: MissionAbandonPatch
Postfix on `GamePlayer.RemoveMission(Mission, bool)`. Fire only when `completed=false`. Emit `Abandoned`.

### ML-T4g: MissionArchivePatch
Postfix on `GamePlayer.ArchiveMission(string, bool)`. Backstop — if no `Completed` event exists for that storyId in the last few game-seconds, emit one. Dedup by storyId proximity.

### ML-T4h: Offer hooks (best-effort, optional at MVP)
Scout + patch the vanilla mission-board populate method and the bar salesman spawn method. Emit `Offered` events. Skip if flaky — `Accepted` is load-bearing, `Offered` is nice-to-have.

### ML-T4i: Plugin.Awake wires everything
Instantiate `ActivityLog`, `GameClock`, `LogIO`, assign to all patch classes. `Harmony.PatchAll` each patch type. Sweep dead sidecars. Register `Application.quitting`.

---

## Phase 5 — Public API facade

### ML-T5a: IMissionLogQuery interface + MissionLogApi facade
Create `Api/IMissionLogQuery.cs` + `Api/MissionLogApi.cs`. Interface exposes R2 query methods using **only primitive + `IReadOnlyList<primitive>` + `IReadOnlyDictionary<string, primitive>`** returns — NO `ActivityEvent` record in the public signature. Events surface as `IReadOnlyDictionary<string, object?>` for neutral reflection access.
**Tests**: facade returns expected keys, `Current` is null before plugin-awake.

### ML-T5b: Wire facade in Plugin.Awake
After `ActivityLog` instantiation, assign `MissionLogApi.Current = new MissionLogQueryAdapter(log)`. Teardown in `OnDestroy`.
**Done when**: a soft-dep reflection probe from another process (or a dummy test fixture) can call the API and get sensible results.

### ML-T5c: Reflection-probe smoke test
Add a test that calls `MissionLogApi.Current` via reflection (using `Type.GetType("VGMissionLog.Api.MissionLogApi, VGMissionLog")`). Asserts the neutral shape works without referencing any VGMissionLog types beyond the facade.

---

## Phase 6 — Config + diagnostics

### ML-T6a: MissionLogConfig
`Config/MissionLogConfig.cs` — BepInEx `ConfigFile` bindings:
- `Logging.Verbose` (bool, default false)
- `Persistence.MaxEvents` (int, default 2000)

### ML-T6b: Wire config
`Plugin.Awake` reads the config, applies `MaxEvents` to the log, gates debug logging on `Verbose`.

---

## Phase 7 — Polish + manual verification

### ML-T7a: README finalization
Update `README.md` with install instructions, known limitations, list of which lifecycle events are captured and which are best-effort.

### ML-T7b: Manual E2E verification
Start a new save, accept a bounty, complete it, accept a patrol, abandon it. Inspect the sidecar JSON — verify every event has correct snapshot fields and timestamps.

### ML-T7c: Third-party consumer smoke test
With at least one reflection-based consumer mod alongside, verify:
- Events authored by that mod are captured.
- The consumer's own state isn't perturbed — this mod observes, never mutates.
- The facade is discoverable via reflection (`Type.GetType("VGMissionLog.Api.MissionLogApi, VGMissionLog")`).

---

## Definition of Done (MVP)

- All ML-T0…ML-T7 tasks complete.
- CI green (if configured).
- Private GitHub repo (`fank/vanguard-galaxy-missionlog`) has at least one tagged release (`v0.1.0`).

## Deferred to post-MVP

- Objective-progression events
- JSON export CLI
- Typed `VGMissionLog.Contracts` package
- Cross-save analytics tooling
- Hangar / personal-hangar activity logging (tracked elsewhere as an idea, not scoped here)
