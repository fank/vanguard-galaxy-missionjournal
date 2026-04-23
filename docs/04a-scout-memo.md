# 04a — Vanilla lifecycle scout memo (ML-T4a)

Exact signatures, calling contexts, and lifecycle ordering of the vanilla methods VGMissionLog patches in Phase 4. All line numbers reference `/tmp/vg-decomp/Assembly-CSharp.decompiled.cs`.

## Save lifecycle

### `Source.Util.SaveGame.Store` (line 22163)

```csharp
public static void Store(
    JsonObject data,
    string saveName,
    SaveGameFormat format = SaveGameFormat.Compressed,
    int attempt = 0)
```

- **Static** method on `SaveGame`. File path is `{SavesDir.FullName}/{saveName}.save` (line 22166).
- Writes either gzip-compressed (default) or pretty JSON.
- On exception, **recursively self-calls** with `attempt+1` up to 5 times (line 22190–22194) before showing `AlertPopup`. ⇒ our postfix will fire **once per call**, so on a failing save we'd emit 5 sidecar flushes. Acceptable — our write is idempotent and we flush the same in-memory snapshot each time.
- **Patch**: postfix. Derive vanilla save path from `saveName` (re-apply `SavesDir.FullName + "/" + saveName + ".save"`) and call `LogIO.Write(LogPathResolver.From(savePath), schema)`. Wrap in try/catch + warn-log per R5.2.

### `Source.Util.SaveGameFile.LoadSaveGame` (line 22283)

```csharp
public void LoadSaveGame()
```

- **Instance** method on `SaveGameFile`. Vanilla save path is `this.File.FullName`.
- Body wraps `SaveGame.LoadState(Recall())` in a try/catch; on failure, shows popup and returns to menu.
- **Patch**: prefix (one prefix allowed per spec R5.1 for save-load). Matches architecture doc §"Dependency on vanilla save lifecycle" — sidecar is populated before any hypothetical post-load consumer queries during vanilla's load body. Read via `LogIO.Read(LogPathResolver.From(this.File.FullName))` → `ActivityLog.LoadFrom(result.Schema.Events)` or empty log on MissingFile/Corrupted/UnsupportedVersion.

## Mission lifecycle

### `Source.Player.GamePlayer.AddMissionWithLog(Mission)` (line 34880)

```csharp
public void AddMissionWithLog(Mission mission)
// NB: string-overload at line 34911 dispatches here after resolving a StoryMission template
```

- Two overloads; we target the `Mission` one. The `string`-overload calls into the `Mission`-overload (TODO: verify by reading body) so patching the latter captures both entry points.
- **Patch**: postfix → emit `Accepted` event.

### `Source.Player.GamePlayer.CompleteMission(Mission, bool)` (line 33932)

```csharp
public void CompleteMission(Mission m, bool force = false)
{
    m.ClaimRewards(force);          // 33934
    // … UI refreshes …
}
// NB: string-overload at line 33912 is independent
```

- **`force` bool**: passed through to `ClaimRewards` as the `force` flag that bypasses `CanClaimRewards()`. Not a success/failure gate.
- Calls `m.ClaimRewards(force)` which:
  1. Iterates `rewards` and calls `reward.OnComplete(this)` (line 36425–36428). Rewards are **not cleared** here — iteration only.
  2. Calls `GamePlayer.current.RemoveMission(this, completed: true)` (line 36429). This triggers `RemoveMission`, which in turn calls `ArchiveMission(storyId, allowDuplicate: true)` if `storyId != null`.
- **Reward capture timing**: `mission.rewards` is still populated when our postfix fires on `CompleteMission`. No prefix needed (contrary to the plan's "check decomp for timing" TODO).
- **Patch**: postfix → read `mission.rewards`, emit `Completed` event with credits/xp/rep.
- **Call-chain side-effects to be aware of** (these fire when `CompleteMission` is called):
  1. Our `Complete` postfix (intentional)
  2. Our `Abandon` postfix sees `RemoveMission(mission, completed: true)` — **must gate on `completed == false`** or we double-emit
  3. Our `Archive` postfix sees `ArchiveMission(storyId, allowDuplicate: true)` — **must dedup against recent `Completed` events** or we double-emit

### `Source.MissionSystem.Mission.MissionFailed(string)` (line 36456)

```csharp
public void MissionFailed(string reason)
{
    failed = true;
    // shows a red notification
}
```

- Sets `failed = true`; does **not** call `RemoveMission` or `ArchiveMission`. Mission stays in player's list until something else archives/abandons it.
- **Patch**: postfix → emit `Failed` event with `reason` captured in a note field.
- A failed mission is typically **followed later** by `RemoveMission(m, completed: false)` when the player abandons it — that'll fire our `Abandon` postfix. Two events for the sequence is semantically correct (Failed = condition hit, Abandoned = player dropped).

### `Source.Player.GamePlayer.RemoveMission(Mission, bool completed)` (line 33883)

```csharp
public void RemoveMission(Mission mission, bool completed)
{
    // … remove from bounty/patrol/industry/missions slot …
    if (!completed) mission.OnMissionAbandoned();
    else if (mission.storyId != null)
        ArchiveMission(mission.storyId, allowDuplicate: true);
}
```

- **`completed` is the 2nd param** (verified).
- Called from two paths:
  1. `Mission.ClaimRewards` → `completed: true` (success path)
  2. Player-initiated abandon UI (presumably) → `completed: false`
- **Patch**: postfix, gated on `completed == false` → emit `Abandoned` event.

### `Source.Player.GamePlayer.ArchiveMission(string, bool allowDuplicate)` (line 33875)

```csharp
public void ArchiveMission(string id, bool allowDuplicate = false)
{
    if (allowDuplicate || !missionsArchive.Contains(id))
        missionsArchive.Add(id);
}
```

- **`allowDuplicate` is NOT a success/failure gate** — it's just "archive even if already there". Called with `true` by the normal complete path (`RemoveMission` → `ArchiveMission`); called with default `false` by the dev-cheat tutorial-skip (line 95144).
- **Patch**: postfix, **backstop only**. Dedup by checking whether a `Completed` event for this `storyId` was emitted in the last few game-seconds (the normal path fires `CompleteMission` → `Complete` event → `ArchiveMission` → `Archive` postfix arrives within milliseconds). Only emit a synthesized `Completed` event when no recent match exists (unusual paths, dev cheats).

## Invariant call-chain summary (a normal complete)

```
CompleteMission(m, false)                    [our Complete postfix emits here]
 └─ m.ClaimRewards(false)
     ├─ foreach reward.OnComplete(this)       [apply credits/XP/rep]
     └─ GamePlayer.current.RemoveMission(m, completed: true)
                                              [our Abandon postfix skips: completed==true]
         └─ ArchiveMission(m.storyId, allowDuplicate: true)
                                              [our Archive postfix skips: recent Completed exists]
```

## Field access cheat-sheet (for the Phase-4 event builder)

The publicized stub's method bodies are `throw null;` IL (see ML-T2c commit), so **public fields** work via direct access but **auto-property getters** NRE under xUnit. Use direct field access for fields, reflect-read `<Name>k__BackingField` for auto-properties.

| Vanilla symbol | Kind | Access in plugin code | In tests |
|---|---|---|---|
| `Mission.name`             | public field       | `mission.name`             | direct |
| `Mission.storyId`          | public field       | `mission.storyId`          | direct |
| `Mission.sourceFaction`    | public field       | `mission.sourceFaction`    | direct |
| `Mission.sourcePoi`        | public field       | `mission.sourcePoi`        | direct (but typed MapPointOfInterest — see below) |
| `Mission.missionItems`     | public field       | `mission.missionItems`     | direct |
| `Mission.failed`           | public field       | `mission.failed`           | direct |
| `Mission.steps`            | auto-prop (get-only, private set) | reflect `<steps>k__BackingField` | reflect |
| `Mission.rewards`          | auto-prop (get-only, private set) | reflect `<rewards>k__BackingField` | reflect |
| `Mission.level`            | auto-prop (computed; calls `GamePlayer.current.level` when dynamicLevel) | unsafe in tests; capture via try/catch + fallback | avoid |
| `MissionStep.objectives`   | auto-prop          | reflect `<objectives>k__BackingField` | reflect |
| `MapPointOfInterest.*`     | various            | TBD per Phase-4 builder scout | TBD |
| `GamePlayer.current`       | static             | direct (null-safe `?.` in plugin code) | null in tests |
| `GamePlayer.current.elapsedTime` | public field | direct via `?.` | 0 default |

`mission.level`'s getter calls `GamePlayer.current.level` when `dynamicLevel == true`, which NREs in tests (GamePlayer.current is null). The event builder reads `mission.level` in a try/catch defaulting to 0, or reads the backing fields for `dynamicLevel` + per-step POI levels directly — TBD in ML-T4aa.

## Harmony gotchas

- Flat patch classes only (one `[HarmonyPatch]` per file). `Harmony.PatchAll(typeof(OuterClass))` silently no-ops when the attribute lives on nested types. We sidestep by one-patch-one-file.
- Wrap every postfix/prefix body in try/catch + `Plugin.Log.LogWarning` per spec R5.2. Never rethrow — vanilla execution must survive our failures.
- Post-`PatchAll`, assert `_harmony.GetPatchedMethods().Count() >= N` in a test fixture to catch silent rename drift (spec R6.2).
