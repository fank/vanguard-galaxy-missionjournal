# 01 — Background & Context

## The problem this mod solves

Mod-authored brokers, LLM-driven NPCs, and narrative systems all benefit enormously from knowing *what the player has actually been doing* — what kind of missions, in which systems, against which factions, how recently. But vanilla Vanguard Galaxy does not persist a queryable activity log: missions are completed, rewards are paid, counters tick up, and the lived history evaporates. Only the coarse `bountyRank` / `patrolRank` / `industryRank` ladders and the `missionsArchive` list of completed storyIds survive, neither of which answers "what has this player been up to in Zoran for the last week?"

The sibling project [VGAnima](../vanguard-galaxy-anima) has proven (via its LLM-authored broker system) that rich per-player narrative context transforms NPC dialogue from generic-procedural to feeling-authored. VGAnima accumulated a journal of VGAnima-broker mission outcomes to power this, but that journal is:

1. **Narrow in scope** — only records VGAnima-authored missions, not vanilla ones. A player who's been grinding vanilla bounties for a week is invisible to VGAnima's brokers.
2. **Coupled to VGAnima's internal schema** — migrations (v1→v2→v3) happen because VGAnima owns both the *data* and its *consumer*. Every consumer-side change risks invalidating historical data.
3. **Load-bearing alongside mutation risk** — VGAnima injects LLM-authored missions; its sidecar schema and mutation patches are shipping constraints on how the journal evolves.

Splitting the observational layer into its own mod cleans all three:

- **Scope** — VGMissionLog hooks vanilla's lifecycle methods directly, capturing ALL missions (vanilla + 3rd-party) uniformly.
- **Schema freedom** — the log's shape is owned by one mod with a single responsibility; consumers read through a stable query API rather than reaching into internal storage.
- **Risk isolation** — pure observer, append-only log, zero vanilla-state mutation. Worst-case corruption is a lost log file, not a broken save.

## The sibling-mod pattern

This repository mirrors the architecture the VG mod ecosystem has already converged on:

| Mod | Role | Relationship |
|---|---|---|
| [VGTTS](../vanguard-galaxy-tts) | TTS audio for dialogue | Pure producer of an audio service |
| [VGAnima](../vanguard-galaxy-anima) | LLM broker + mission authoring | Mutator; soft-deps on VGTTS |
| **VGMissionLog** (this) | Mission activity observer | Pure observer; consumers soft-dep on this |

All three share:

- BepInEx 5.x + HarmonyX 2.10 plugin shape
- `.netstandard2.1` target, Unity 6000.2 runtime
- `Assembly-CSharp.dll` publicized stub symlinked from a known sibling at build time
- `<save>.<modname>.json` sidecar pattern for per-save persistence
- Soft-dependency registration via `[BepInDependency(..., SoftDependency)]` — never crashes when an optional peer is missing

## Data collection now, consumer later

A crucial value proposition: **VGMissionLog can and should ship before its primary consumer is ready.** The worst case for late consumers is an empty log when they first query it — historically-benign. The best case is that VGMissionLog runs quietly for weeks/months in the wild, and when a consumer (VGAnima, a future stats dashboard, a third-party progression mod) wants activity data, it's simply *there*, accurate and rich.

This is why the spec emphasizes a stable query API and versioned log format from day one: breaking changes to the log shape after users have accumulated history are costly in a way that VGAnima's internal schema migrations are not.

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

- Not a mission-authoring mod (that's VGAnima's job).
- Not a UI / HUD / dashboard (future separate consumer).
- Not a stats aggregator for real-time display (just provides the raw query substrate).
- Not an achievement system (a consumer could build one on top).
- Not a cross-save meta-database (one log per save slot, same lifecycle as the vanilla save).
