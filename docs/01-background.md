# 01 — Background & Context

## The problem this mod solves

Mod-authored brokers, LLM-driven NPCs, and narrative systems all benefit enormously from knowing *what the player has actually been doing* — what kind of missions, in which systems, against which factions, how recently. But vanilla Vanguard Galaxy does not persist a queryable activity log: missions are completed, rewards are paid, counters tick up, and the lived history evaporates. Only the coarse `bountyRank` / `patrolRank` / `industryRank` ladders and the `missionsArchive` list of completed storyIds survive, neither of which answers "what has this player been up to in Zoran for the last week?"

VGMissionLog fills that gap as a pure-observer companion plugin:

- **Hooks vanilla's mission lifecycle directly** — captures every accept / complete / fail / abandon uniformly, whether the mission came from the vanilla mission board or from another mod.
- **Schema owned by one mod with a single responsibility** — consumers read through a stable query API rather than reaching into internal storage.
- **Pure observer, append-only log, zero vanilla-state mutation.** Worst-case corruption is a lost log file, not a broken save.

## The sibling-mod pattern

This repository follows the architecture the VG mod ecosystem has converged on:

| Mod | Role |
|---|---|
| [VGTTS](../vanguard-galaxy-tts) | TTS audio for dialogue |
| **VGMissionLog** (this) | Mission activity observer — pure observer; consumers soft-dep on this |

Shared conventions:

- BepInEx 5.x + HarmonyX 2.10 plugin shape
- `.netstandard2.1` target, Unity 6000.2 runtime
- `Assembly-CSharp.dll` publicized stub symlinked from a known sibling at build time
- `<save>.<modname>.json` sidecar pattern for per-save persistence
- Soft-dependency registration via `[BepInDependency(..., SoftDependency)]` — never crashes when an optional peer is missing

## Data collection now, consumer later

A crucial value proposition: **VGMissionLog can and should ship before any consumer is ready.** The worst case for a late consumer is an empty log when it first queries — historically-benign. The best case is that VGMissionLog runs quietly for weeks or months in the wild, and when a consumer (a stats dashboard, a progression mod, a narrative system) wants activity data, it's simply *there*, accurate and rich.

This is why the spec emphasizes a stable query API and versioned log format from day one: breaking changes to the log shape after users have accumulated history are costly.

## Observational-only, not read-only

"Observes only" is the core invariant. VGMissionLog:

- **MAY** read vanilla state in its Harmony postfixes.
- **MAY** write to its own sidecar file.
- **MAY** expose read APIs to other mods.
- **MUST NOT** mutate any vanilla runtime state (no field writes, no method overrides, no Prefix patches that change behavior, no Harmony state tampering).
- **MUST NOT** touch vanilla's save file.
- **MUST NOT** inject UI, NPCs, missions, or content.

This discipline is what makes it safe to install alongside anything and leave running indefinitely. Any feature that violates this invariant — no matter how narratively compelling — belongs in a different mod.

## Non-goals

- Not a mission-authoring mod.
- Not a UI / HUD / dashboard (future separate consumer).
- Not a stats aggregator for real-time display (just provides the raw query substrate).
- Not an achievement system (a consumer could build one on top).
- Not a cross-save meta-database (one log per save slot, same lifecycle as the vanilla save).
