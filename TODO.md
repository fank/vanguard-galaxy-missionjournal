# TODO

## Raw objective accessors

Design and expose accessors that let consumers query events by the raw
mission-objective types the game emits (e.g. `KillEnemies`, `TravelToPOI`,
`CollectItemTypes`, `ProtectUnit`, `Mining`). The monitor stops at
surfacing the game's own objective type names — consumers decide what to
call a mission based on its objectives.

**Before implementing, inventory:**

- What objective fields does the game hand us per type? (e.g.
  `CollectItemTypes.itemCategory` — consumers may want to filter by
  "collect ore" vs "collect salvage" without re-inferring.)
- Which objective lists matter: per-step objectives only, or do we
  surface step metadata too?
- Primitive-dict shape: `objectives: ["KillEnemies", "TravelToPOI"]`
  (bare strings) vs structured `[{type, ...fields}]`. Bare strings are
  the thinnest contract and match the "no interpretation" rule; add
  fields only when a consumer concretely needs them.

**Candidate accessors (adapter + ActivityLog):**

- `GetEventsWithObjective(string objectiveTypeName, ...)` — events whose
  `objectives` list contains `objectiveTypeName` (ordinal exact match).
- Objective data on the event dict under key `objectives`.

**Sources of the reflection read:** already exists (the builder reaches
`Mission.<steps>k__BackingField` and `MissionStep.<objectives>k__BackingField`
— previously used by the deleted ArchetypeInferrer). Reuse that read, but
record objective type names directly instead of bucketing into categories.
