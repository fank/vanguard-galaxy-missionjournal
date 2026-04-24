# VGMissionJournal — Observational mission-activity logger for Vanguard Galaxy

A BepInEx 5 plugin that records every mission-lifecycle transition — acceptance, completion, failure, abandonment — of every mission the player engages with, whether authored by vanilla or another mod. The journal is persisted per save, exposes a reflection-friendly public query API for other mods to consume, and **never mutates vanilla state**.

## Design principle: pure observer

VGMissionJournal observes; it never mutates. Worst failure mode is a corrupt journal file the player can safely delete — history is lost, the save is not. This is why it's safe to leave running indefinitely alongside anything.

Consumers (future stats dashboards, LLM-driven NPCs, progression mods, etc.) soft-dep via reflection on a stable API surface. Consumers bucket and interpret; VGMissionJournal records what the game hands it and nothing more.

## Install

1. Install BepInEx 5 for Vanguard Galaxy.
2. Drop `VGMissionJournal.dll` into `<GameDir>/BepInEx/plugins/VGMissionJournal/`.
3. Launch the game. On first save, a paired `<saveName>.save.vgmissionjournal.json` appears next to the vanilla `.save` file.

Config lives at `<GameDir>/BepInEx/config/vgmissionjournal.cfg` after first run:

```ini
[Journal]
## When true, emit a Debug-level log line for every captured mission lifecycle transition.
# Default: false
Verbose = false

## Soft cap on retained missions per save (FIFO eviction, oldest-accepted first). 0 = unbounded.
# Default: 2000
MaxMissions = 2000
```

## What's captured

| Timeline state | Recorded when | Confidence |
|---|---|---|
| **Accepted**  | Player accepts a mission (vanilla's `GamePlayer.AddMissionWithLog`)              | load-bearing |
| **Completed** | Player turns in a mission (vanilla's `GamePlayer.CompleteMission`) + rewards    | load-bearing |
| **Failed**    | Mission fail condition triggers (vanilla's `Mission.MissionFailed`)             | load-bearing |
| **Abandoned** | Player drops a mission (vanilla's `RemoveMission(_, completed:false)`)          | load-bearing |
| *(Archived backstop)* | Synthesized Completed for unusual paths (dev cheats, swallow errors) | backstop |

Every captured mission carries: in-game accept timestamp + wall-clock, storyId, a session-local mission instance id (for correlating across the accept→complete lifecycle when `storyId` is empty), mission name + raw subclass name (`mission.GetType().Name`), source station / system / faction, a full snapshot of the step/objective tree (type per objective), a unified rewards list covering all 14 vanilla reward subtypes, a timeline of state transitions (Accepted → Completed/Failed/Abandoned), and a player-state snapshot. Consumers bucket by subclass / objective type if they want categories; VGMissionJournal does not classify.

See [`docs/api.md`](docs/api.md) for the full mission schema and method reference.

## For modders

Querying mission history from your own plugin takes about ten lines. Quick sketch:

```csharp
using VGMissionJournal.Api;
using VGMissionJournal.Logging;  // MissionRecord + Outcome live here

if (MissionJournalApi.Current is { } api)
    foreach (var m in api.GetRecentMissions(10))
        Logger.LogInfo($"{m.MissionSubclass} {m.Outcome?.ToString() ?? "active"}");
```

Full integration guide (typed reference, reflection fallback, soft-dep guard), method reference, mission field schema, and sidecar format: **[`docs/api.md`](docs/api.md)**.

## Known gaps

- **No per-step or per-objective transition events.** Vanilla has no hook: `MissionStep.isComplete` is a computed getter over `objectives.All(IsComplete)`, and `Mission.currentStep` just scans for the first non-complete step — nothing fires when a step or objective flips. The timeline captures mission-level transitions only (Accepted → Completed/Failed/Abandoned); the step/objective tree on the Accepted snapshot is structural, not progress-tracked.
- **No Offered tracking.** Not captured; if consumers ever need it, it's additive.
- **A few snapshot fields are never populated** — `missionLevel`, sector IDs, target station/system, player ship. Vanilla's accessor graph is deeper than what's currently scouted; fields are reserved in the schema.
- **One sidecar per save.** No cross-save aggregation.

Full list: [`docs/api.md#known-gaps`](docs/api.md#known-gaps).

## Safety invariants

VGMissionJournal is a **pure observer**. The Harmony patch set is postfix-only, with one allowed prefix on save-load for timing. Every patch body is wrapped in try/catch and warn-logs on failure — vanilla execution must survive our internal state. If the sidecar is missing or corrupt, the journal starts empty and vanilla loads normally; the corrupt file is quarantined with a UTC timestamp for forensic inspection.

## Build

```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet build VGMissionJournal.sln -c Debug
DOTNET_ROLL_FORWARD=LatestMajor dotnet test VGMissionJournal.Tests/VGMissionJournal.Tests.csproj --no-build -c Debug
```

Or via the Makefile:

```bash
make link-asm    # symlink Assembly-CSharp.dll from sibling VGTTS checkout
make build
make test
make deploy      # copies the DLL into <GameDir>/BepInEx/plugins/VGMissionJournal/
```

## License

MIT — see [`LICENSE`](LICENSE).
