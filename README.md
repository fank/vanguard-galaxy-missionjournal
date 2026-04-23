# VGMissionLog — Observational mission-activity logger for Vanguard Galaxy

A BepInEx plugin that records every mission-lifecycle event — acceptance, completion, failure, abandonment — of every mission the player engages with (vanilla bounty / patrol / industry / story missions AND 3rd-party-mod-authored missions). The log is persisted per save, exposes a public query API for other mods to consume, and **never mutates vanilla state**.

## Why a separate mod

VGMissionLog is intentionally split from its primary consumer, [VGAnima](../vanguard-galaxy-anima), on the principle "observers live apart from mutators":

- VGAnima injects LLM-authored missions and brokers — it carries real save-corruption risk (schema migrations, orphans, sidecar versioning).
- VGMissionLog observes only. Its worst failure mode is a corrupt log file the player can safely delete, losing history but not the save.

Splitting also means you can **install VGMissionLog today** (long before VGAnima is stable) and start accumulating real player-activity data. When VGAnima matures and consumes VGMissionLog's API, it'll have months of history rather than starting empty.

Follows the same soft-dependency pattern as VGTTS ↔ VGAnima: either mod works standalone; installing both upgrades the combined experience.

## What it does

- Hooks vanilla's mission lifecycle (`AddMissionWithLog` / `CompleteMission` / `MissionFailed` / `RemoveMission` / `ArchiveMission`) via Harmony postfixes.
- Records one `ActivityEvent` per transition, with full contextual detail (see [`docs/02-requirements.md`](docs/02-requirements.md)).
- Flushes to a paired sidecar `<save>.vgmissionlog.json` on vanilla save-write.
- Exposes a C# query API other mods can reach via reflection:
  - Events in a specific system / within a jumpgate radius / by faction / by outcome / by time window
  - Aggregate counts (missions-by-archetype, most-active-system, etc.)

## Status

**Pre-implementation.** This repository contains specification documents only. Implementation will follow the plan in [`docs/04-implementation-plan.md`](docs/04-implementation-plan.md).

## Docs

- [`docs/01-background.md`](docs/01-background.md) — why this mod exists, how it fits with VGAnima + VGTTS, risk isolation rationale.
- [`docs/02-requirements.md`](docs/02-requirements.md) — what must be captured, event schema, query API surface, persistence contract.
- [`docs/03-architecture.md`](docs/03-architecture.md) — proposed design: Harmony hooks, classification logic, storage shape, versioning.
- [`docs/04-implementation-plan.md`](docs/04-implementation-plan.md) — ordered atomic tasks an implementation agent can execute top-to-bottom.

## License

TBD (will match sibling mods when implementation begins).
