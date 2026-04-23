# VGMissionLog — Observational mission-activity logger for Vanguard Galaxy

A BepInEx 5 plugin that records every mission-lifecycle event — acceptance, completion, failure, abandonment — of every mission the player engages with (vanilla bounty / patrol / industry / story missions AND third-party-mod-authored missions). The log is persisted per save, exposes a reflection-friendly public query API for other mods to consume, and **never mutates vanilla state**.

## Why a separate mod

VGMissionLog is intentionally split from its primary consumer, [VGAnima](../vanguard-galaxy-anima), on the principle "observers live apart from mutators":

- VGAnima injects LLM-authored missions and brokers — it carries real save-corruption risk (schema migrations, orphans, sidecar versioning).
- VGMissionLog observes only. Its worst failure mode is a corrupt log file the player can safely delete, losing history but not the save.

Splitting also means you can **install VGMissionLog today** (long before VGAnima is stable) and start accumulating real player-activity data. When VGAnima matures and consumes VGMissionLog's API, it'll have months of history rather than starting empty.

Follows the same soft-dependency pattern as [VGTTS](../vanguard-galaxy-tts): either mod works standalone; installing both upgrades the combined experience.

## Install

1. Install BepInEx 5 for Vanguard Galaxy.
2. Drop `VGMissionLog.dll` into `<GameDir>/BepInEx/plugins/VGMissionLog/`.
3. Launch the game. On first save, a paired `<saveName>.save.vgmissionlog.json` appears next to the vanilla `.save` file.

Config lives at `<GameDir>/BepInEx/config/vgmissionlog.cfg` after first run:

```ini
[Logging]
## When true, emit a Debug-level log line for every captured mission lifecycle event.
# Default: false
Verbose = false

[Persistence]
## Soft cap on retained events per save (FIFO eviction). Each event is ~500 bytes serialised.
# Default: 2000
MaxEvents = 2000
```

## What's captured

| Lifecycle event | Emitted when | Confidence |
|---|---|---|
| **Accepted**  | Player accepts a mission (vanilla's `GamePlayer.AddMissionWithLog`)              | load-bearing |
| **Completed** | Player turns in a mission (vanilla's `GamePlayer.CompleteMission`) + rewards    | load-bearing |
| **Failed**    | Mission fail condition triggers (vanilla's `Mission.MissionFailed`)             | load-bearing |
| **Abandoned** | Player drops a mission (vanilla's `RemoveMission(_, completed:false)`)          | load-bearing |
| **Archived**  | Backstop — synthesized Completed for unusual paths (dev cheats, swallow errors) | backstop     |
| **Offered**   | —                                                                               | **deferred** — post-MVP; Accepted covers the signal consumers actually want |
| **ObjectiveProgressed** | —                                                                     | **deferred** — post-MVP; additive schema extension |

Every captured event carries: event id, in-game timestamp + wall-clock, storyId, mission name + classified type + raw subclass name, activity archetype (Combat / Gather / Salvage / Deliver / Escort, inferred best-effort), outcome (for terminals), source station / system / faction, facility origin (BountyBoard / PoliceBoard / IndustryBoard), rewards (credits / experience / reputation on Completed), and a player-state snapshot.

See [`docs/02-requirements.md`](docs/02-requirements.md) for the full per-event shape.

## Querying from another mod

VGMissionLog exposes `VGMissionLog.Api.MissionLogApi.Current` — a static facade returning `null` when the plugin isn't loaded. Consumers soft-dep via reflection:

```csharp
var facade = Type.GetType("VGMissionLog.Api.MissionLogApi, VGMissionLog");
var query  = facade?.GetProperty("Current")?.GetValue(null);
if (query is null) return;  // plugin not installed

var method = query.GetType().GetMethod("GetEventsInSystem",
    new[] { typeof(string), typeof(double), typeof(double) });
var events = (IReadOnlyList<IReadOnlyDictionary<string, object?>>)
    method!.Invoke(query, new object[] { "sys-guid-here", 0.0, double.MaxValue })!;

foreach (var evt in events)
{
    var storyId   = (string)evt["storyId"]!;
    var type      = (string)evt["type"]!;
    // ...
}
```

The API surface returns only primitives, strings, `IReadOnlyList<IReadOnlyDictionary<string, object?>>`, and `IReadOnlyDictionary<string, int>` — no consumer-side reference to VGMissionLog's internal record types is needed. See [`IMissionLogQuery`](VGMissionLog/Api/IMissionLogQuery.cs) for the full method list.

## Known limitations (MVP)

- **No Offered events.** Vanilla's offer paths vary by source (board regeneration, bar salesman spawn, broker dispatch) and are harder to hook reliably. Accepted is load-bearing for consumer queries; Offered is deferred to post-MVP as an additive enhancement.
- **No ObjectiveProgressed events.** Partial mission progress (step completion) isn't captured at v1. Follows the same additive-schema path when it lands.
- **`missionLevel` is always 0.** Vanilla's `Mission.level` getter chains through `GamePlayer.current`, which makes it safe to read at runtime but causes test-time reflection issues. Shipping 0 until a safer accessor is wired.
- **Sector / target-station / player-ship snapshots are null.** Same reason — vanilla's accessor graph is deeper than we've fully scouted. Additive fields; consumers already treat them as nullable.
- **Reputation rewards use the captured faction identifier only.** Faction name and display metadata aren't persisted — consumers resolve display strings from their own faction data if needed.
- **One sidecar per save.** No cross-save aggregation; each save's log is independent and shares the save file's lifecycle.

## Safety invariants

VGMissionLog is a **pure observer**. The Harmony patch set is postfix-only, with one allowed prefix on save-load (per spec R5.1). Every patch body is wrapped in try/catch and warn-logs on failure — vanilla execution must survive our internal state. If the sidecar is missing or corrupt, the log starts empty and vanilla loads normally; the corrupt file is quarantined with a UTC timestamp for forensic inspection.

## Docs

- [`docs/01-background.md`](docs/01-background.md) — why this mod exists, how it fits with VGAnima + VGTTS, risk isolation rationale.
- [`docs/02-requirements.md`](docs/02-requirements.md) — what must be captured, event schema, query API surface, persistence contract.
- [`docs/03-architecture.md`](docs/03-architecture.md) — proposed design: Harmony hooks, classification logic, storage shape, versioning.
- [`docs/04-implementation-plan.md`](docs/04-implementation-plan.md) — ordered atomic tasks an implementation agent can execute top-to-bottom.
- [`docs/04a-scout-memo.md`](docs/04a-scout-memo.md) — vanilla method signatures, call-chain ordering, publicized-stub access patterns.

## Build

```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet build VGMissionLog.sln -c Debug
DOTNET_ROLL_FORWARD=LatestMajor dotnet test VGMissionLog.Tests/VGMissionLog.Tests.csproj --no-build -c Debug
```

Or via the Makefile:

```bash
make link-asm    # symlink Assembly-CSharp.dll from sibling VGTTS checkout
make build
make test
make deploy      # copies the DLL into <GameDir>/BepInEx/plugins/VGMissionLog/
```

## License

TBD (will match sibling mods).
