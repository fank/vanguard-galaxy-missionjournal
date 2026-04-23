# VGMissionLog — Observational mission-activity logger for Vanguard Galaxy

A BepInEx 5 plugin that records every mission-lifecycle event — acceptance, completion, failure, abandonment — of every mission the player engages with, whether authored by vanilla or another mod. The log is persisted per save, exposes a reflection-friendly public query API for other mods to consume, and **never mutates vanilla state**.

## Design principle: pure observer

VGMissionLog observes; it never mutates. Worst failure mode is a corrupt log file the player can safely delete — history is lost, the save is not. This is why it's safe to leave running indefinitely alongside anything.

Consumers (future stats dashboards, LLM-driven NPCs, progression mods, etc.) soft-dep via reflection on a stable API surface. Consumers bucket and interpret; VGMissionLog records what the game hands it and nothing more.

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

Every captured event carries: event id, in-game timestamp + wall-clock, storyId, mission name + raw subclass name (`mission.GetType().Name`), outcome (for terminals), source station / system / faction, rewards (credits / experience / reputation on Completed), and a player-state snapshot. Consumers bucket by subclass name if they want mission-type categories; VGMissionLog does not classify.

See [`docs/02-requirements.md`](docs/02-requirements.md) for the full per-event shape.

## Querying from another mod

VGMissionLog exposes `VGMissionLog.Api.MissionLogApi.Current` — a static facade returning `null` when the plugin isn't loaded.

### Typed reference (recommended)

Drop `VGMissionLog.dll` into your consumer plugin's `libs/` folder and add a `<Reference>` in your csproj. Your calls are then just ordinary C#:

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
        // Soft-dep guard: skip the typed path entirely when VGMissionLog
        // isn't installed. Putting the usage in a separate method keeps the
        // JIT from resolving VGMissionLog types until this branch is taken.
        if (Chainloader.PluginInfos.ContainsKey("vgmissionlog"))
            UseMissionLog();
    }

    private void UseMissionLog()
    {
        if (MissionLogApi.Current is not { } api) return;

        foreach (var evt in api.GetEventsInSystem("sys-guid-here"))
        {
            var storyId = (string)evt["storyId"]!;
            var type    = (string)evt["type"]!;
            // ...
        }
    }
}
```

Events come back as `IReadOnlyList<IReadOnlyDictionary<string, object?>>` with camelCase string keys — consumers access fields by key rather than by property. See [`IMissionLogQuery`](VGMissionLog/Api/IMissionLogQuery.cs) for the full method list.

### Reflection fallback (zero compile-time dep)

If you prefer not to reference `VGMissionLog.dll` at all (scripting-style mods, dynamic-language consumers), the same facade works via reflection:

```csharp
var facade = Type.GetType("VGMissionLog.Api.MissionLogApi, VGMissionLog");
var query  = facade?.GetProperty("Current")?.GetValue(null);
if (query is null) return;  // plugin not installed

var method = query.GetType().GetMethod("GetEventsInSystem",
    new[] { typeof(string), typeof(double), typeof(double) });
var events = (IReadOnlyList<IReadOnlyDictionary<string, object?>>)
    method!.Invoke(query, new object[] { "sys-guid-here", 0.0, double.MaxValue })!;
```

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

- [`docs/01-background.md`](docs/01-background.md) — why this mod exists, risk isolation rationale.
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
