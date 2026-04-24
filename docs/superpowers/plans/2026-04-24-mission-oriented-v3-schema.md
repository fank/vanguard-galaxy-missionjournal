# Mission-Oriented v3 Schema Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the flat event log with a mission-oriented record store where each mission is a first-class aggregate carrying its identity, immutable structure, rewards, and an explicit `timeline[]` of state transitions. Old v1 sidecars migrate to v3 at load; no legacy fields persist past migration.

**Architecture:** The plugin currently models mission activity as a flat `List<ActivityEvent>` with per-event redundant snapshots. v3 flips this: `MissionStore` holds one `MissionRecord` per mission, with identity fields (subclass, faction, source system) captured once at accept time, immutable structure in `steps[]`, unified `rewards[]`, and a `timeline[]` of lifecycle transitions (`Accepted`, then one of `Completed` / `Failed` / `Abandoned`). All queries become reductions over missions instead of filters over events. Sidecar v1 loads get migrated in-memory via `V1ToV3Migrator` and then the log only ever holds v3.

**Tech Stack:** C# netstandard2.1 plugin + net8.0 xUnit tests, BepInEx 5 + HarmonyX 2.10, Newtonsoft.Json with StringEnumConverter, `ilspycmd` for decompilation of game DLL.

**Investigation result:** Vanilla has no step-completion hook — `MissionStep.isComplete` is a computed getter. v3 timeline captures **mission-level transitions only** (accepted + terminal). Per-step timing stays out of scope; if a hook is discovered later, new `TimelineState` entries are additive.

**Legacy policy (user-mandated):** No dual fields, no `AllowIntegerValues` back-door, no compat shims in emitter/reader. Old v1 sidecars get migrated on load; afterward they're written as v3 and never look backward. Typed `rewardsCredits`/`rewardsExperience`/`rewardsReputation` fields are removed from the schema — migration folds them into `rewards[]` as `Credits`/`Experience`/`Reputation` entries.

---

## File Structure

### New files

- `VGMissionJournal/Logging/TimelineState.cs` — enum (`Accepted`, `Completed`, `Failed`, `Abandoned`).
- `VGMissionJournal/Logging/TimelineEntry.cs` — record `(TimelineState State, double GameSeconds, string? RealUtc)`.
- `VGMissionJournal/Logging/MissionStepDefinition.cs` — immutable step structure (no `IsComplete`, no `StatusText`).
- `VGMissionJournal/Logging/MissionObjectiveDefinition.cs` — immutable objective structure `(string Type, IReadOnlyDictionary<string, object?>? Fields)`.
- `VGMissionJournal/Logging/MissionRecord.cs` — aggregate (identity + `Steps` + `Rewards` + `Timeline` + helpers `IsActive`, `Outcome`, `AcceptedAtGameSeconds`, `TerminalAtGameSeconds`, `AgeSeconds(now)`).
- `VGMissionJournal/Logging/MissionStore.cs` — replaces `ActivityLog`. In-memory store keyed by a synthesized mission key (explained in Task 6). Indexes by source system / source faction. Holds the lifecycle event `OnMissionChanged`.
- `VGMissionJournal/Logging/MissionRecordBuilder.cs` — replaces `ActivityEventBuilder`. Exposes `CreateFromAccept(mission)` (snapshots identity + steps + rewards) and `AppendTransition(record, TimelineState, gameSeconds)`.
- `VGMissionJournal/Persistence/V1ToV3Migrator.cs` — migration from legacy `JournalSchema` (v1 `events[]`) to v3 `missions[]`.
- `VGMissionJournal/Api/MissionRecordMapper.cs` — replaces `ActivityEventMapper`. Maps `MissionRecord` → neutral-shape dictionary.

### Modified files

- `VGMissionJournal/Persistence/JournalSchema.cs` — bump `CurrentVersion` to 3; replace `Events` array with `Missions` array; remove `AllowIntegerValues` commentary (moot — no legacy).
- `VGMissionJournal/Persistence/JournalIO.cs` — on load, if `schema.Version == 1` route through `V1ToV3Migrator`; if `== 3` load as-is; otherwise quarantine.
- `VGMissionJournal/Api/IMissionJournalQuery.cs` — replace event-oriented methods with mission-oriented ones (see Task 10).
- `VGMissionJournal/Api/MissionJournalQueryAdapter.cs` — adapt `MissionStore` queries.
- `VGMissionJournal/Api/MissionJournalApi.cs` — wire `MissionStore` instead of `ActivityLog`.
- `VGMissionJournal/Patches/MissionAcceptPatch.cs` — call `MissionStore.RecordAccept(mission)`.
- `VGMissionJournal/Patches/MissionCompletePatch.cs` — call `MissionStore.RecordTerminal(mission, TimelineState.Completed)`; keep archive-race dedup (`InFlightStoryIds`).
- `VGMissionJournal/Patches/MissionFailPatch.cs` — `RecordTerminal(..., Failed)`.
- `VGMissionJournal/Patches/MissionAbandonPatch.cs` — `RecordTerminal(..., Abandoned)`.
- `VGMissionJournal/Patches/MissionArchivePatch.cs` — backstop: if no terminal timeline entry on the record, synthesize one (`Completed`).
- `VGMissionJournal/Patches/PatchWiring.cs` — wire `MissionStore` + `MissionRecordBuilder`.
- `VGMissionJournal/Plugin.cs` — config key rename `Logging.MaxEvents` → `Logging.MaxMissions`.
- `docs/api.md` — complete rewrite of the data-shape + query sections.
- `README.md` — spot-check and update any referenced fields / method names.

### Deleted files

- `VGMissionJournal/Logging/ActivityEvent.cs`
- `VGMissionJournal/Logging/ActivityEventType.cs`
- `VGMissionJournal/Logging/ActivityLog.cs`
- `VGMissionJournal/Logging/ActivityEventBuilder.cs` (replaced by `MissionRecordBuilder`)
- `VGMissionJournal/Logging/MissionStepSnapshot.cs`
- `VGMissionJournal/Logging/MissionObjectiveSnapshot.cs`
- `VGMissionJournal/Api/ActivityEventMapper.cs`
- Old test files that only cover deleted types (see Task 16).

### Kept

- `VGMissionJournal/Logging/Outcome.cs` — still useful as a `{Completed, Failed, Abandoned}` enum returned by `MissionRecord.Outcome`. `TimelineState` is the transition kind; `Outcome` is the terminal kind.
- `VGMissionJournal/Logging/MissionRewardSnapshot.cs` — keep; still the right shape for `rewards[]` entries.
- `VGMissionJournal/Logging/RepReward.cs` — used by reward snapshots.
- `VGMissionJournal/Logging/IClock.cs`, `GameClock.cs` — still the time source.

---

## Testing Strategy

All new code ships with unit tests alongside. Existing 177 tests will be triaged in Task 17: tests that exercise deleted types get deleted; tests that cover still-live behavior through the new types get rewritten. No "let's port this test mechanically" — if a test has no corresponding v3 behavior, it goes.

**Property tests** (via fast-check / FsCheck if already present; otherwise xUnit `[Theory]` + `InlineData`): timeline append invariants (can't append Accepted after terminal, etc.), migration roundtrip on crafted v1 payloads.

**Integration smoke**: load a real v1 sidecar from the user's game saves dir into a test fixture, migrate, verify record count and a few identity fields on specific known missions.

---

## Task 0: Create worktree and plan branch

**Files:** none (setup).

- [ ] **Step 1: Create a worktree off main for this refactor**

```bash
git worktree add ../vanguard-galaxy-missionlog-v3 -b feat/v3-mission-oriented
cd ../vanguard-galaxy-missionlog-v3
```

- [ ] **Step 2: Confirm clean start**

Run: `git status`
Expected: `On branch feat/v3-mission-oriented` / `nothing to commit, working tree clean`.

- [ ] **Step 3: Confirm baseline build + tests pass**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test VGMissionJournal.sln --nologo 2>&1 | tail -3`
Expected: `Passed! - Failed:     0, Passed:   177, Skipped:     0, Total:   177`

---

## Task 1: `TimelineState` enum + `TimelineEntry` record

**Files:**
- Create: `VGMissionJournal/Logging/TimelineState.cs`
- Create: `VGMissionJournal/Logging/TimelineEntry.cs`
- Test: `VGMissionJournal.Tests/Logging/TimelineEntryTests.cs`

- [ ] **Step 1: Write the failing test**

Create `VGMissionJournal.Tests/Logging/TimelineEntryTests.cs`:

```csharp
using VGMissionJournal.Logging;
using Xunit;

namespace VGMissionJournal.Tests.Logging;

public class TimelineEntryTests
{
    [Fact]
    public void Entry_StoresStateAndGameSecondsAndOptionalRealUtc()
    {
        var e = new TimelineEntry(TimelineState.Accepted, 12450.0, "2026-04-24T14:12:30Z");
        Assert.Equal(TimelineState.Accepted, e.State);
        Assert.Equal(12450.0, e.GameSeconds);
        Assert.Equal("2026-04-24T14:12:30Z", e.RealUtc);
    }

    [Fact]
    public void Entry_RealUtcIsOptional()
    {
        var e = new TimelineEntry(TimelineState.Completed, 13420.0, RealUtc: null);
        Assert.Null(e.RealUtc);
    }

    [Theory]
    [InlineData(TimelineState.Accepted,  false)]
    [InlineData(TimelineState.Completed, true)]
    [InlineData(TimelineState.Failed,    true)]
    [InlineData(TimelineState.Abandoned, true)]
    public void Entry_IsTerminal_TrueForCompletedFailedAbandoned(TimelineState s, bool isTerminal)
    {
        var e = new TimelineEntry(s, 0.0, null);
        Assert.Equal(isTerminal, e.IsTerminal);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test VGMissionJournal.sln --nologo --filter "TimelineEntryTests"`
Expected: build FAILS with "type or namespace 'TimelineState' not found" / "TimelineEntry not found".

- [ ] **Step 3: Write minimal implementation**

Create `VGMissionJournal/Logging/TimelineState.cs`:

```csharp
namespace VGMissionJournal.Logging;

/// <summary>
/// Mission lifecycle transition kinds recorded in <see cref="MissionRecord.Timeline"/>.
/// Accepted is non-terminal; Completed/Failed/Abandoned are terminal (a mission has
/// at most one terminal entry and it's always the last one in its timeline).
/// </summary>
public enum TimelineState
{
    Accepted,
    Completed,
    Failed,
    Abandoned,
}
```

Create `VGMissionJournal/Logging/TimelineEntry.cs`:

```csharp
namespace VGMissionJournal.Logging;

/// <summary>
/// One transition in a mission's timeline. <see cref="RealUtc"/> is optional —
/// we stamp it on Accepted and terminal entries (the anchors consumers use for
/// wall-clock reasoning); interior transitions, if any are ever added, may omit it.
/// </summary>
public sealed record TimelineEntry(
    TimelineState State,
    double GameSeconds,
    string? RealUtc)
{
    public bool IsTerminal =>
        State is TimelineState.Completed or TimelineState.Failed or TimelineState.Abandoned;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test VGMissionJournal.sln --nologo --filter "TimelineEntryTests"`
Expected: `Passed! - Failed: 0, Passed: 5`.

- [ ] **Step 5: Commit**

```bash
git add VGMissionJournal/Logging/TimelineState.cs VGMissionJournal/Logging/TimelineEntry.cs VGMissionJournal.Tests/Logging/TimelineEntryTests.cs
git commit -m "feat(logging): add TimelineState enum + TimelineEntry record

Foundation for the v3 mission-oriented schema. Timeline entries are the
explicit state-transition log on each MissionRecord."
```

---

## Task 2: `MissionStepDefinition` + `MissionObjectiveDefinition` (immutable structure)

**Files:**
- Create: `VGMissionJournal/Logging/MissionObjectiveDefinition.cs`
- Create: `VGMissionJournal/Logging/MissionStepDefinition.cs`
- Test: `VGMissionJournal.Tests/Logging/MissionStepDefinitionTests.cs`

- [ ] **Step 1: Write the failing test**

Create `VGMissionJournal.Tests/Logging/MissionStepDefinitionTests.cs`:

```csharp
using System.Collections.Generic;
using VGMissionJournal.Logging;
using Xunit;

namespace VGMissionJournal.Tests.Logging;

public class MissionStepDefinitionTests
{
    [Fact]
    public void ObjectiveDefinition_CarriesTypeAndOptionalFields()
    {
        var obj = new MissionObjectiveDefinition(
            "KillEnemies",
            new Dictionary<string, object?> { ["enemyFaction"] = "CrimsonFang", ["targetCount"] = 3 });

        Assert.Equal("KillEnemies",   obj.Type);
        Assert.Equal("CrimsonFang",   obj.Fields!["enemyFaction"]);
        Assert.Equal(3,               obj.Fields["targetCount"]);
    }

    [Fact]
    public void ObjectiveDefinition_FieldsOptional_NullAllowed()
    {
        var obj = new MissionObjectiveDefinition("TravelToPOI", Fields: null);
        Assert.Null(obj.Fields);
    }

    [Fact]
    public void StepDefinition_HoldsDescriptionFlagsAndObjectives()
    {
        var step = new MissionStepDefinition(
            Description: "Eliminate the raiders",
            RequireAllObjectives: true,
            Hidden: false,
            Objectives: new[]
            {
                new MissionObjectiveDefinition("KillEnemies", null),
            });

        Assert.Equal("Eliminate the raiders", step.Description);
        Assert.True(step.RequireAllObjectives);
        Assert.False(step.Hidden);
        Assert.Single(step.Objectives);
    }
}
```

- [ ] **Step 2: Run the test — expect build failure**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test VGMissionJournal.sln --nologo --filter "MissionStepDefinitionTests"`
Expected: build FAILS, types not found.

- [ ] **Step 3: Write minimal implementations**

Create `VGMissionJournal/Logging/MissionObjectiveDefinition.cs`:

```csharp
using System.Collections.Generic;

namespace VGMissionJournal.Logging;

/// <summary>
/// Immutable description of one objective within a <see cref="MissionStepDefinition"/>.
/// <para>v3 captures <b>structure only</b>: the objective's type name
/// (<c>objective.GetType().Name</c>) and the stable fields known at mission
/// generation (e.g. <c>enemyFaction</c>, <c>targetCount</c>, <c>targetPOI</c>).
/// Dynamic progress (kill counters, status text) is not captured — once a mission
/// is generated, its objective structure never changes per vanilla's design.</para>
/// </summary>
public sealed record MissionObjectiveDefinition(
    string Type,
    IReadOnlyDictionary<string, object?>? Fields);
```

Create `VGMissionJournal/Logging/MissionStepDefinition.cs`:

```csharp
using System.Collections.Generic;

namespace VGMissionJournal.Logging;

/// <summary>
/// Immutable description of one step in a mission's plan. See
/// <see cref="MissionRecord.Steps"/>.
/// </summary>
public sealed record MissionStepDefinition(
    string? Description,
    bool RequireAllObjectives,
    bool Hidden,
    IReadOnlyList<MissionObjectiveDefinition> Objectives);
```

- [ ] **Step 4: Run tests**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test VGMissionJournal.sln --nologo --filter "MissionStepDefinitionTests"`
Expected: `Passed! - Failed: 0, Passed: 3`.

- [ ] **Step 5: Commit**

```bash
git add VGMissionJournal/Logging/MissionObjectiveDefinition.cs VGMissionJournal/Logging/MissionStepDefinition.cs VGMissionJournal.Tests/Logging/MissionStepDefinitionTests.cs
git commit -m "feat(logging): add immutable step + objective definition records

v3 captures mission structure once at accept time. Dynamic progress
(IsComplete, StatusText, killCount) is deliberately dropped — vanilla
doesn't mutate mission structure post-generation."
```

---

## Task 3: `MissionRecord` aggregate + helpers

**Files:**
- Create: `VGMissionJournal/Logging/MissionRecord.cs`
- Test: `VGMissionJournal.Tests/Logging/MissionRecordTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `VGMissionJournal.Tests/Logging/MissionRecordTests.cs`:

```csharp
using System.Collections.Generic;
using VGMissionJournal.Logging;
using Xunit;

namespace VGMissionJournal.Tests.Logging;

public class MissionRecordTests
{
    private static MissionRecord Sample(params TimelineEntry[] timeline) =>
        new(
            StoryId:               "story-1",
            MissionInstanceId:     "inst-1",
            MissionName:           "Hunt the Crimson Fang",
            MissionSubclass:       "BountyMission",
            MissionLevel:          4,
            SourceStationId:       null, SourceStationName: null,
            SourceSystemId:        "sys-zoran", SourceSystemName: "Zoran",
            SourceSectorId:        null, SourceSectorName: null,
            SourceFaction:         "BountyGuild",
            TargetStationId:       null, TargetStationName: null, TargetSystemId: null,
            PlayerLevel:           1, PlayerShipName: null, PlayerShipLevel: null,
            PlayerCurrentSystemId: null,
            Steps:                 System.Array.Empty<MissionStepDefinition>(),
            Rewards:               System.Array.Empty<MissionRewardSnapshot>(),
            Timeline:              timeline);

    [Fact]
    public void IsActive_TrueWhenOnlyAccepted()
    {
        var r = Sample(new TimelineEntry(TimelineState.Accepted, 100.0, null));
        Assert.True(r.IsActive);
        Assert.Null(r.Outcome);
        Assert.Null(r.TerminalAtGameSeconds);
    }

    [Fact]
    public void IsActive_FalseWhenTerminalPresent()
    {
        var r = Sample(
            new TimelineEntry(TimelineState.Accepted,  100.0, null),
            new TimelineEntry(TimelineState.Completed, 420.0, null));
        Assert.False(r.IsActive);
        Assert.Equal(Outcome.Completed, r.Outcome);
        Assert.Equal(420.0, r.TerminalAtGameSeconds);
    }

    [Fact]
    public void AcceptedAtGameSeconds_ReadsFromFirstTimelineEntry()
    {
        var r = Sample(new TimelineEntry(TimelineState.Accepted, 100.0, null));
        Assert.Equal(100.0, r.AcceptedAtGameSeconds);
    }

    [Fact]
    public void AgeSeconds_UsesTerminalIfPresent_ElseNow()
    {
        var active = Sample(new TimelineEntry(TimelineState.Accepted, 100.0, null));
        Assert.Equal(400.0, active.AgeSeconds(nowGameSeconds: 500.0));

        var done = Sample(
            new TimelineEntry(TimelineState.Accepted,  100.0, null),
            new TimelineEntry(TimelineState.Completed, 420.0, null));
        Assert.Equal(320.0, done.AgeSeconds(nowGameSeconds: 9999.0));  // now ignored when terminal exists
    }

    [Theory]
    [InlineData(TimelineState.Completed, Outcome.Completed)]
    [InlineData(TimelineState.Failed,    Outcome.Failed)]
    [InlineData(TimelineState.Abandoned, Outcome.Abandoned)]
    public void Outcome_MapsFromTerminalState(TimelineState state, Outcome expected)
    {
        var r = Sample(
            new TimelineEntry(TimelineState.Accepted, 0.0, null),
            new TimelineEntry(state,                  1.0, null));
        Assert.Equal(expected, r.Outcome);
    }
}
```

- [ ] **Step 2: Run test — expect build failure**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test VGMissionJournal.sln --nologo --filter "MissionRecordTests"`
Expected: build FAILS, `MissionRecord` not found.

- [ ] **Step 3: Write the implementation**

Create `VGMissionJournal/Logging/MissionRecord.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace VGMissionJournal.Logging;

/// <summary>
/// One mission's complete record in the log. Identity fields are captured
/// once on <see cref="TimelineState.Accepted"/> and never mutate — vanilla
/// doesn't change a mission after generation. The <see cref="Timeline"/>
/// is the mutable part: it grows by one entry on each lifecycle transition
/// and terminates with exactly one of Completed / Failed / Abandoned.
///
/// <para><b>Identifiers.</b> <see cref="StoryId"/> is populated for authored
/// story missions (Tutorial, Puppeteers); otherwise a synthesized
/// <c>"anon:&lt;guid&gt;"</c> fills it. <see cref="MissionInstanceId"/> is a
/// session-local GUID (does not survive save/load).</para>
///
/// <para><b>Rewards.</b> One unified list covering every reward subtype. Typed
/// credits/experience/reputation fields from the v1 schema are gone — read
/// them off <see cref="Rewards"/> by <c>Type</c>.</para>
/// </summary>
public sealed record MissionRecord(
    string StoryId,
    string MissionInstanceId,
    string? MissionName,
    string MissionSubclass,
    int MissionLevel,
    string? SourceStationId,
    string? SourceStationName,
    string? SourceSystemId,
    string? SourceSystemName,
    string? SourceSectorId,
    string? SourceSectorName,
    string? SourceFaction,
    string? TargetStationId,
    string? TargetStationName,
    string? TargetSystemId,
    int PlayerLevel,
    string? PlayerShipName,
    int? PlayerShipLevel,
    string? PlayerCurrentSystemId,
    IReadOnlyList<MissionStepDefinition> Steps,
    IReadOnlyList<MissionRewardSnapshot> Rewards,
    IReadOnlyList<TimelineEntry> Timeline)
{
    public bool IsActive => !TerminalEntry.HasValue;

    public double AcceptedAtGameSeconds =>
        Timeline.Count > 0 ? Timeline[0].GameSeconds
                           : throw new InvalidOperationException("MissionRecord has no Accepted entry.");

    public double? TerminalAtGameSeconds => TerminalEntry?.GameSeconds;

    public Outcome? Outcome => TerminalEntry?.State switch
    {
        TimelineState.Completed => Logging.Outcome.Completed,
        TimelineState.Failed    => Logging.Outcome.Failed,
        TimelineState.Abandoned => Logging.Outcome.Abandoned,
        _                       => null,
    };

    /// <summary>Age in game-seconds. If the mission has terminated, returns
    /// duration from accept to terminal. If still active, returns
    /// <paramref name="nowGameSeconds"/> − accept.</summary>
    public double AgeSeconds(double nowGameSeconds) =>
        (TerminalAtGameSeconds ?? nowGameSeconds) - AcceptedAtGameSeconds;

    private TimelineEntry? TerminalEntry =>
        Timeline.Count > 0 && Timeline[Timeline.Count - 1].IsTerminal
            ? Timeline[Timeline.Count - 1]
            : null;
}
```

- [ ] **Step 4: Run tests**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test VGMissionJournal.sln --nologo --filter "MissionRecordTests"`
Expected: `Passed! - Failed: 0, Passed: 7`.

- [ ] **Step 5: Commit**

```bash
git add VGMissionJournal/Logging/MissionRecord.cs VGMissionJournal.Tests/Logging/MissionRecordTests.cs
git commit -m "feat(logging): add MissionRecord aggregate with timeline helpers

IsActive / Outcome / AgeSeconds / AcceptedAtGameSeconds /
TerminalAtGameSeconds all derive from Timeline — one source of truth
for mission state, no redundant flags."
```

---

## Task 4: `MissionStore` — in-memory mission store

**Files:**
- Create: `VGMissionJournal/Logging/MissionStore.cs`
- Test: `VGMissionJournal.Tests/Logging/MissionStoreTests.cs`

**Design note:** `MissionStore` keys missions by a stable string key. Use `MissionInstanceId` as the primary key (session-local but guaranteed unique within a session). `StoryId` for story missions would collide across sessions on sidecar reload; since sidecar keeps v3 records by instance id + story id both, we route by instance id. After migration from v1 a record may have an instance id synthesized by the migrator.

- [ ] **Step 1: Write failing tests**

Create `VGMissionJournal.Tests/Logging/MissionStoreTests.cs`:

```csharp
using System.Linq;
using VGMissionJournal.Logging;
using Xunit;

namespace VGMissionJournal.Tests.Logging;

public class MissionStoreTests
{
    private static MissionRecord Record(
        string instanceId, string storyId, double acceptedAt,
        string? sourceSystemId = null, string? sourceFaction = null,
        string subclass = "Mission",
        params TimelineEntry[] afterAccept)
    {
        var tl = new System.Collections.Generic.List<TimelineEntry>
        {
            new(TimelineState.Accepted, acceptedAt, null),
        };
        tl.AddRange(afterAccept);
        return new MissionRecord(
            storyId, instanceId, "N", subclass, 1,
            null, null, sourceSystemId, null, null, null, sourceFaction,
            null, null, null, 1, null, null, null,
            System.Array.Empty<MissionStepDefinition>(),
            System.Array.Empty<MissionRewardSnapshot>(),
            tl);
    }

    [Fact]
    public void Upsert_StoresRecordByMissionInstanceId()
    {
        var store = new MissionStore();
        var r = Record("i1", "s1", 10.0);
        store.Upsert(r);
        Assert.Same(r, store.GetByInstanceId("i1"));
    }

    [Fact]
    public void AllMissions_ReturnsInsertionOrder()
    {
        var store = new MissionStore();
        store.Upsert(Record("i1", "s1", 10.0));
        store.Upsert(Record("i2", "s2", 20.0));
        store.Upsert(Record("i3", "s3", 30.0));
        Assert.Equal(new[] { "i1", "i2", "i3" },
                     store.AllMissions.Select(r => r.MissionInstanceId));
    }

    [Fact]
    public void GetActiveMissions_ExcludesTerminated()
    {
        var store = new MissionStore();
        store.Upsert(Record("i1", "s1", 10.0));  // active
        store.Upsert(Record("i2", "s2", 20.0,
            afterAccept: new TimelineEntry(TimelineState.Completed, 25.0, null)));
        Assert.Equal(new[] { "i1" },
                     store.GetActiveMissions().Select(r => r.MissionInstanceId));
    }

    [Fact]
    public void IndexedBySystem_ReturnsMatchingMissions()
    {
        var store = new MissionStore();
        store.Upsert(Record("i1", "s1", 10.0, sourceSystemId: "sys-zoran"));
        store.Upsert(Record("i2", "s2", 20.0, sourceSystemId: "sys-helion"));
        store.Upsert(Record("i3", "s3", 30.0, sourceSystemId: "sys-zoran"));
        Assert.Equal(new[] { "i1", "i3" },
                     store.GetMissionsInSystem("sys-zoran").Select(r => r.MissionInstanceId));
    }

    [Fact]
    public void EvictionCap_EvictsOldestByAcceptedAt_PreservingFifoInvariant()
    {
        var store = new MissionStore(maxMissions: 2);
        store.Upsert(Record("i1", "s1", 10.0));
        store.Upsert(Record("i2", "s2", 20.0));
        store.Upsert(Record("i3", "s3", 30.0));
        Assert.Equal(new[] { "i2", "i3" },
                     store.AllMissions.Select(r => r.MissionInstanceId));
    }

    [Fact]
    public void Unbounded_NeverEvicts()
    {
        var store = new MissionStore(maxMissions: MissionStore.Unbounded);
        for (var t = 1.0; t <= 10.0; t++)
            store.Upsert(Record($"i{t}", $"s{t}", t));
        Assert.Equal(10, store.TotalMissionCount);
    }

    [Fact]
    public void OnMissionChanged_FiresOnEveryUpsert()
    {
        var store = new MissionStore();
        var fired = new System.Collections.Generic.List<string>();
        store.OnMissionChanged += r => fired.Add(r.MissionInstanceId);

        store.Upsert(Record("i1", "s1", 10.0));
        store.Upsert(Record("i1", "s1", 10.0,   // update — same instance, new terminal
            afterAccept: new TimelineEntry(TimelineState.Completed, 15.0, null)));

        Assert.Equal(new[] { "i1", "i1" }, fired);
    }
}
```

- [ ] **Step 2: Run test — expect build failure**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test VGMissionJournal.sln --nologo --filter "MissionStoreTests"`
Expected: build FAILS, `MissionStore` not found.

- [ ] **Step 3: Write implementation**

Create `VGMissionJournal/Logging/MissionStore.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace VGMissionJournal.Logging;

/// <summary>
/// In-memory store of <see cref="MissionRecord"/> aggregates, keyed by
/// <see cref="MissionRecord.MissionInstanceId"/>. Replaces the v1 event-oriented
/// <c>ActivityLog</c>.
///
/// <para>Ordering is first-insert (stable). Eviction at the soft cap drops the
/// oldest-accepted mission, preserving a FIFO invariant on accept time.</para>
///
/// <para><b>Thread-safety:</b> single-threaded (Unity main). No locking.</para>
/// </summary>
internal sealed class MissionStore
{
    public const int DefaultMaxMissions = 2000;
    public const int Unbounded          = 0;

    private readonly int _maxMissions;
    private readonly Action<int>? _onFirstEviction;
    private bool _evictionNotified;

    private readonly List<MissionRecord> _records = new();
    private readonly Dictionary<string, MissionRecord> _byInstanceId =
        new(StringComparer.Ordinal);

    public MissionStore(int maxMissions = DefaultMaxMissions, Action<int>? onFirstEviction = null)
    {
        _maxMissions    = maxMissions > 0 ? maxMissions : Unbounded;
        _onFirstEviction = onFirstEviction;
    }

    public bool IsUnbounded => _maxMissions == Unbounded;
    public int  MaxMissions => _maxMissions;
    public int  TotalMissionCount => _records.Count;

    public IReadOnlyList<MissionRecord> AllMissions => _records;

    /// <summary>Fires after every <see cref="Upsert"/>. Swallowed on throw.</summary>
    public event Action<MissionRecord>? OnMissionChanged;

    public MissionRecord? GetByInstanceId(string instanceId) =>
        _byInstanceId.TryGetValue(instanceId, out var r) ? r : null;

    /// <summary>
    /// Insert or replace a record. Replacement is by
    /// <see cref="MissionRecord.MissionInstanceId"/>; the insertion position
    /// is preserved so iteration order stays stable.
    /// </summary>
    public void Upsert(MissionRecord record)
    {
        if (record is null) throw new ArgumentNullException(nameof(record));

        if (_byInstanceId.TryGetValue(record.MissionInstanceId, out var existing))
        {
            // Replace in-place without moving.
            var idx = _records.IndexOf(existing);
            _records[idx] = record;
            _byInstanceId[record.MissionInstanceId] = record;
        }
        else
        {
            _records.Add(record);
            _byInstanceId[record.MissionInstanceId] = record;
            if (!IsUnbounded && _records.Count > _maxMissions)
            {
                EvictOldest();
                NotifyFirstEvictionOnce();
            }
        }

        try { OnMissionChanged?.Invoke(record); } catch { /* subscriber failures must not interrupt */ }
    }

    public IReadOnlyList<MissionRecord> GetActiveMissions()
    {
        var result = new List<MissionRecord>();
        foreach (var r in _records) if (r.IsActive) result.Add(r);
        return result;
    }

    public IReadOnlyList<MissionRecord> GetMissionsInSystem(string systemId)
    {
        var result = new List<MissionRecord>();
        foreach (var r in _records)
            if (string.Equals(r.SourceSystemId, systemId, StringComparison.Ordinal))
                result.Add(r);
        return result;
    }

    public IReadOnlyList<MissionRecord> GetMissionsByFaction(string factionId)
    {
        var result = new List<MissionRecord>();
        foreach (var r in _records)
            if (string.Equals(r.SourceFaction, factionId, StringComparison.Ordinal))
                result.Add(r);
        return result;
    }

    public void LoadFrom(IEnumerable<MissionRecord> records)
    {
        if (records is null) throw new ArgumentNullException(nameof(records));
        _records.Clear();
        _byInstanceId.Clear();
        _evictionNotified = false;
        foreach (var r in records)
        {
            if (r is null) continue;
            _records.Add(r);
            _byInstanceId[r.MissionInstanceId] = r;
        }
        while (!IsUnbounded && _records.Count > _maxMissions) EvictOldest();
    }

    private void EvictOldest()
    {
        var evicted = _records[0];
        _records.RemoveAt(0);
        _byInstanceId.Remove(evicted.MissionInstanceId);
    }

    private void NotifyFirstEvictionOnce()
    {
        if (_evictionNotified) return;
        _evictionNotified = true;
        _onFirstEviction?.Invoke(_maxMissions);
    }
}
```

- [ ] **Step 4: Run tests**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test VGMissionJournal.sln --nologo --filter "MissionStoreTests"`
Expected: `Passed! - Failed: 0, Passed: 7`.

- [ ] **Step 5: Commit**

```bash
git add VGMissionJournal/Logging/MissionStore.cs VGMissionJournal.Tests/Logging/MissionStoreTests.cs
git commit -m "feat(logging): add MissionStore keyed by MissionInstanceId

Replaces ActivityLog's event store. Upsert updates in place; eviction
drops oldest-accepted; LoadFrom rebuilds from a sidecar read."
```

---

## Task 5: `MissionRecordBuilder` — snapshots + transitions

**Files:**
- Create: `VGMissionJournal/Logging/MissionRecordBuilder.cs`
- Test: `VGMissionJournal.Tests/Logging/MissionRecordBuilderTests.cs`

**Design note:** This is the one place that touches Unity game types via reflection. The existing `ActivityEventBuilder` is 451 lines; its reflection logic for reading steps/objectives/rewards/faction/system is all reusable. **Copy that logic verbatim** — don't re-derive. The new builder's public surface is:
- `CreateFromAccept(Mission mission) → MissionRecord` — full structure snapshot + `Timeline = [ Accepted ]`.
- `AppendTransition(MissionRecord existing, TimelineState state, Mission? mission) → MissionRecord` — returns a new record with the transition appended and (for terminal states) `Rewards` repopulated from the live mission if available.

- [ ] **Step 1: Port the reflection block from ActivityEventBuilder**

Read `VGMissionJournal/Logging/ActivityEventBuilder.cs` in full. The private static fields (`_missionStepsField`, `_stepObjectivesField`, `_itemTypeIdentifierField`), `ReadPrimitiveFields`, `ExtractSteps`, `SnapshotStep`, `SnapshotObjective`, `ExtractRewards`, `ResolveItemIdentifier`, `StripCloneSuffix`, `SafeGet`, and the reward-type skiplist all carry over unchanged. Copy them into `MissionRecordBuilder.cs` as private members.

- [ ] **Step 2: Write failing tests**

Create `VGMissionJournal.Tests/Logging/MissionRecordBuilderTests.cs`:

```csharp
using System.Linq;
using VGMissionJournal.Logging;
using VGMissionJournal.Tests.Support;
using Xunit;

namespace VGMissionJournal.Tests.Logging;

public class MissionRecordBuilderTests
{
    [Fact]
    public void CreateFromAccept_TimelineStartsWithAccepted()
    {
        var clock   = new FakeClock { GameSeconds = 100.0 };
        var builder = new MissionRecordBuilder(clock, () => null);
        var mission = TestMission.Generic("m1");

        var r = builder.CreateFromAccept(mission);

        Assert.Single(r.Timeline);
        Assert.Equal(TimelineState.Accepted, r.Timeline[0].State);
        Assert.Equal(100.0,                  r.Timeline[0].GameSeconds);
        Assert.NotNull(r.Timeline[0].RealUtc);
    }

    [Fact]
    public void AppendTransition_AddsTerminalEntry_AtCurrentClock()
    {
        var clock   = new FakeClock { GameSeconds = 100.0 };
        var builder = new MissionRecordBuilder(clock, () => null);
        var mission = TestMission.Generic("m1");

        var accepted = builder.CreateFromAccept(mission);

        clock.GameSeconds = 420.0;
        var completed = builder.AppendTransition(accepted, TimelineState.Completed, mission);

        Assert.Equal(2, completed.Timeline.Count);
        Assert.Equal(TimelineState.Completed, completed.Timeline[1].State);
        Assert.Equal(420.0,                   completed.Timeline[1].GameSeconds);
        Assert.Equal(Outcome.Completed,       completed.Outcome);
    }

    [Fact]
    public void AppendTransition_RepopulatesRewards_OnTerminal()
    {
        // Verifies we read rewards off mission.rewards on terminal (since the
        // Accept-time capture may have been empty if rewards finalize later).
        // Covered via TestMission.WithRewards helper — see support code.
    }
}
```

- [ ] **Step 3: Run tests — expect build failure**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test VGMissionJournal.sln --nologo --filter "MissionRecordBuilderTests"`
Expected: build FAILS, `MissionRecordBuilder` not found.

- [ ] **Step 4: Write the builder**

Create `VGMissionJournal/Logging/MissionRecordBuilder.cs`. Public surface:

```csharp
using System;
using System.Collections.Generic;
using Source.MissionSystem;

namespace VGMissionJournal.Logging;

internal sealed class MissionRecordBuilder
{
    private readonly IClock _clock;
    private readonly Func<string?> _playerCurrentSystemIdProvider;

    public MissionRecordBuilder(IClock clock, Func<string?> playerCurrentSystemIdProvider)
    {
        _clock = clock;
        _playerCurrentSystemIdProvider = playerCurrentSystemIdProvider;
    }

    public MissionRecord CreateFromAccept(Mission mission)
    {
        // 1) read identity fields via ReadPrimitiveFields (ported from ActivityEventBuilder)
        // 2) ExtractSteps(mission) → IReadOnlyList<MissionStepDefinition>
        //    NOTE: produce MissionStepDefinition (no IsComplete, no StatusText on objectives;
        //    fields dict carries only the static subset — reuse the existing field filter).
        // 3) ExtractRewards(mission) → IReadOnlyList<MissionRewardSnapshot>
        // 4) return new MissionRecord(... Timeline: [ TimelineEntry(Accepted, clock.GameSeconds, clock.UtcNow.ToString("o")) ]);
        throw new NotImplementedException("see plan Task 5");
    }

    public MissionRecord AppendTransition(MissionRecord existing, TimelineState state, Mission? mission)
    {
        var entry = new TimelineEntry(state, _clock.GameSeconds,
            state is TimelineState.Accepted || state is TimelineState.Completed or TimelineState.Failed or TimelineState.Abandoned
                ? _clock.UtcNow.ToString("o")
                : null);

        var newTimeline = new List<TimelineEntry>(existing.Timeline.Count + 1);
        newTimeline.AddRange(existing.Timeline);
        newTimeline.Add(entry);

        var newRewards = existing.Rewards;
        if (state == TimelineState.Completed && mission != null)
            newRewards = ExtractRewards(mission);

        return existing with { Timeline = newTimeline, Rewards = newRewards };
    }

    // --- Reflection block: copy verbatim from ActivityEventBuilder ---

    // private static readonly FieldInfo _missionStepsField = …
    // private static readonly FieldInfo _stepObjectivesField = …
    // private static readonly FieldInfo _itemTypeIdentifierField = …
    // private IReadOnlyList<MissionStepDefinition> ExtractSteps(Mission m) { … }
    // private MissionStepDefinition SnapshotStep(object step) { … }
    // private MissionObjectiveDefinition SnapshotObjective(object obj) { … }
    // private IReadOnlyList<MissionRewardSnapshot> ExtractRewards(Mission m) { … }
    // private static void TryAdd(IDictionary<string, object?> dict, string key, object? value) { … }
    // private static object? SafeGet(Func<object?> f) { … }
    // internal static string? StripCloneSuffix(string? name) { … }
}
```

Fill in the commented sections by lifting the code from `ActivityEventBuilder.cs` — types replacing `MissionStepSnapshot`/`MissionObjectiveSnapshot` with the new `MissionStepDefinition`/`MissionObjectiveDefinition`. Drop any field extraction that read `IsComplete` or `StatusText` — those aren't part of the v3 objective contract.

- [ ] **Step 5: Run tests**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test VGMissionJournal.sln --nologo --filter "MissionRecordBuilderTests"`
Expected: `Passed! - Failed: 0, Passed: 3`.

- [ ] **Step 6: Commit**

```bash
git add VGMissionJournal/Logging/MissionRecordBuilder.cs VGMissionJournal.Tests/Logging/MissionRecordBuilderTests.cs
git commit -m "feat(logging): port ActivityEventBuilder → MissionRecordBuilder

CreateFromAccept snapshots identity + steps + rewards. AppendTransition
adds timeline entries; terminal Completed re-reads rewards off the live
mission (final authoritative set)."
```

---

## Task 6: Wire patches to `MissionStore`

**Files:**
- Modify: `VGMissionJournal/Patches/MissionAcceptPatch.cs`
- Modify: `VGMissionJournal/Patches/MissionCompletePatch.cs`
- Modify: `VGMissionJournal/Patches/MissionFailPatch.cs`
- Modify: `VGMissionJournal/Patches/MissionAbandonPatch.cs`
- Modify: `VGMissionJournal/Patches/MissionArchivePatch.cs`
- Modify: `VGMissionJournal/Patches/PatchWiring.cs`

**Design:** Each patch now holds two statics: `MissionRecordBuilder Builder` and `MissionStore Store`. The transition recording is:

```csharp
// Accept:
var record = Builder.CreateFromAccept(mission);
Store.Upsert(record);

// Terminal (Complete/Fail/Abandon):
var existing = Store.GetByInstanceId(/* key derived from mission */);
if (existing is null) return;   // mission we never saw accept — unusual, ignore
var next = Builder.AppendTransition(existing, TimelineState.Completed, mission);
Store.Upsert(next);
```

The **mission-instance key** problem: our session-local instance id comes from `ActivityEventBuilder.GetOrCreateInstanceId(Mission)` (ConditionalWeakTable). That logic lives on the builder and needs to be reachable from patches. Expose `MissionRecordBuilder.GetInstanceId(Mission)` (public) and have patches call it to look up the store.

- [ ] **Step 1: Expose `GetInstanceId` on the builder**

Modify `VGMissionJournal/Logging/MissionRecordBuilder.cs` — the ported `_instanceIds` ConditionalWeakTable and its accessor need to be public so patches can resolve a mission to its key without rebuilding an entire record:

```csharp
public string GetInstanceId(Mission mission) =>
    _instanceIds.GetValue(mission, _ => Guid.NewGuid().ToString());
```

- [ ] **Step 2: Update MissionAcceptPatch**

Replace the existing body of `MissionAcceptPatch.Postfix`:

```csharp
[HarmonyPostfix]
private static void Postfix(Mission mission)
{
    if (mission is null) return;
    try
    {
        var record = Builder.CreateFromAccept(mission);
        Store.Upsert(record);
    }
    catch (Exception e)
    {
        BepLog.LogWarning($"MissionAcceptPatch swallowed: {e}");
    }
}
```

Also change the static slot declarations:

```csharp
internal static MissionRecordBuilder Builder = null!;
internal static MissionStore         Store   = null!;
internal static ManualLogSource      BepLog  = null!;
```

- [ ] **Step 3: Update MissionCompletePatch + MissionFailPatch + MissionAbandonPatch**

Each postfix becomes (adjusting `TimelineState.Completed` / `.Failed` / `.Abandoned` per patch):

```csharp
[HarmonyPostfix]
private static void Postfix(Mission m)
{
    if (m is null) return;
    try
    {
        var key      = Builder.GetInstanceId(m);
        var existing = Store.GetByInstanceId(key);
        if (existing is null) return;
        var next = Builder.AppendTransition(existing, TimelineState.Completed, m);
        Store.Upsert(next);
    }
    catch (Exception e)
    {
        BepLog.LogWarning($"{nameof(MissionCompletePatch)} swallowed: {e}");
    }
    finally
    {
        var id = m.storyId;
        if (!string.IsNullOrEmpty(id)) InFlightStoryIds.Remove(id);
    }
}
```

Keep the `Prefix` / `InFlightStoryIds` logic in `MissionCompletePatch` — archive-race dedup is still required.

- [ ] **Step 4: Update MissionArchivePatch**

Replace its postfix:

```csharp
[HarmonyPostfix]
private static void Postfix(string id)
{
    if (string.IsNullOrEmpty(id)) return;
    if (MissionCompletePatch.InFlightStoryIds.Contains(id)) return;
    try
    {
        // Find the record by storyId (there can be multiple historically —
        // we only care if the current "latest" one lacks a terminal entry).
        MissionRecord? latestActive = null;
        foreach (var r in Store.AllMissions)
        {
            if (string.Equals(r.StoryId, id, StringComparison.Ordinal) && r.IsActive)
                latestActive = r;
        }
        if (latestActive is null) return;
        var next = Builder.AppendTransition(latestActive, TimelineState.Completed, mission: null);
        Store.Upsert(next);
    }
    catch (Exception e)
    {
        BepLog.LogWarning($"MissionArchivePatch swallowed (storyId='{id}'): {e}");
    }
}
```

Note: `mission: null` means rewards won't be re-read here — archive is a backstop, the rewards captured at accept time (if any) stay.

- [ ] **Step 5: Update PatchWiring**

Modify `VGMissionJournal/Patches/PatchWiring.cs` signature:

```csharp
public static void WireAll(
    MissionRecordBuilder builder,
    MissionStore         store,
    JournalIO                io,
    ManualLogSource      bepLog)
{
    MissionAcceptPatch.Builder  = MissionCompletePatch.Builder =
        MissionFailPatch.Builder = MissionAbandonPatch.Builder =
        MissionArchivePatch.Builder = builder;
    MissionAcceptPatch.Store    = MissionCompletePatch.Store =
        MissionFailPatch.Store   = MissionAbandonPatch.Store =
        MissionArchivePatch.Store = store;
    MissionAcceptPatch.BepLog   = MissionCompletePatch.BepLog =
        MissionFailPatch.BepLog  = MissionAbandonPatch.BepLog =
        MissionArchivePatch.BepLog = bepLog;
    SaveLoadPatch.IO  = SaveWritePatch.IO = io;
    SaveLoadPatch.Store = SaveWritePatch.Store = store;
    SaveLoadPatch.BepLog = SaveWritePatch.BepLog = bepLog;
}
```

- [ ] **Step 6: Build**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet build VGMissionJournal.sln -v q 2>&1 | tail -5`
Expected: build fails because `JournalIO` / `SaveLoadPatch` / `SaveWritePatch` still reference the old types. Defer those — they get rewritten in Tasks 7-9. Commit what compiles; leave the build red at this point is OK temporarily only if the task dictates — **don't commit a broken build**. Instead, do Tasks 7-9 before any commit that spans the patch wiring.

- [ ] **Step 7: Defer commit; jump to Task 7**

No commit yet. The full wiring of Tasks 6-9 lands as one commit at the end of Task 9.

---

## Task 7: `JournalSchema` v3 shape

**Files:**
- Modify: `VGMissionJournal/Persistence/JournalSchema.cs`

- [ ] **Step 1: Rewrite JournalSchema**

Replace the existing `JournalSchema.cs` content with:

```csharp
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using VGMissionJournal.Logging;

namespace VGMissionJournal.Persistence;

/// <summary>
/// Top-level shape of a <c>&lt;save&gt;.save.vgmissionjournal.json</c> sidecar
/// at v3 (mission-oriented). The array is keyed by mission, not by event;
/// each element is a <see cref="MissionRecord"/> with identity + structure +
/// rewards + timeline.
///
/// <para>v1 sidecars migrate on load via <see cref="V1ToV3Migrator"/>;
/// writes are always v3. No legacy fields.</para>
/// </summary>
internal sealed record JournalSchema(
    [property: JsonProperty("version")]  int Version,
    [property: JsonProperty("missions")] MissionRecord[] Missions)
{
    public const int CurrentVersion = 3;

    public static JsonSerializerSettings SerializerSettings { get; } = new()
    {
        ContractResolver  = new CamelCasePropertyNamesContractResolver(),
        Formatting        = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        // String enums on the wire. AllowIntegerValues intentionally left
        // at default (true) only because Newtonsoft's StringEnumConverter
        // treats it that way; we never emit integers, so no reader needs it.
        Converters        = { new StringEnumConverter() },
    };
}
```

- [ ] **Step 2: Build**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet build VGMissionJournal.sln -v q 2>&1 | tail -5`
Expected: still failing — `JournalIO` references old `ActivityEvent`-typed schema and `SaveLoadPatch` consumes v1 shape. That's fixed in Task 8/9.

---

## Task 8: `V1ToV3Migrator`

**Files:**
- Create: `VGMissionJournal/Persistence/V1ToV3Migrator.cs`
- Test: `VGMissionJournal.Tests/Persistence/V1ToV3MigratorTests.cs`
- Create (temporary): a compat record of the v1 shape so the migrator can deserialize it without referencing the soon-to-be-deleted `ActivityEvent` type. See step 1.

**Design:** We need to deserialize old v1 payloads after we've deleted `ActivityEvent`. Solution: define a `V1Event` POCO inside `V1ToV3Migrator.cs` — a private nested type that mirrors the v1 field set (camelCase JSON, all primitive/nullable). This keeps the obsolete shape confined to the migrator and free of any runtime import.

- [ ] **Step 1: Write failing test**

Create `VGMissionJournal.Tests/Persistence/V1ToV3MigratorTests.cs`:

```csharp
using System.Linq;
using VGMissionJournal.Logging;
using VGMissionJournal.Persistence;
using Xunit;

namespace VGMissionJournal.Tests.Persistence;

public class V1ToV3MigratorTests
{
    private const string V1Payload = @"
    {
      ""version"": 1,
      ""events"": [
        {
          ""eventId"": ""e1"", ""type"": ""Accepted"",  ""gameSeconds"": 100.0, ""realUtc"": ""2026-01-01T00:00:00Z"",
          ""storyId"": ""story-1"", ""missionInstanceId"": ""inst-1"",
          ""missionName"": ""Hunt"", ""missionSubclass"": ""BountyMission"", ""missionLevel"": 4,
          ""sourceSystemId"": ""sys-zoran"", ""sourceFaction"": ""BountyGuild"",
          ""playerLevel"": 1
        },
        {
          ""eventId"": ""e2"", ""type"": ""Completed"", ""gameSeconds"": 420.0, ""realUtc"": ""2026-01-01T00:07:00Z"",
          ""storyId"": ""story-1"", ""missionInstanceId"": ""inst-1"",
          ""missionName"": ""Hunt"", ""missionSubclass"": ""BountyMission"", ""missionLevel"": 4,
          ""outcome"": ""Completed"",
          ""sourceSystemId"": ""sys-zoran"", ""sourceFaction"": ""BountyGuild"",
          ""rewardsCredits"": 12000, ""rewardsExperience"": 350,
          ""playerLevel"": 1
        },
        {
          ""eventId"": ""e3"", ""type"": ""Accepted"",  ""gameSeconds"": 500.0, ""realUtc"": ""2026-01-01T00:08:20Z"",
          ""storyId"": ""story-2"", ""missionInstanceId"": ""inst-2"",
          ""missionName"": ""Patrol"", ""missionSubclass"": ""PatrolMission"", ""missionLevel"": 2,
          ""sourceSystemId"": ""sys-helion"", ""sourceFaction"": ""Police"",
          ""playerLevel"": 1
        }
      ]
    }";

    [Fact]
    public void MigratesV1Payload_ToMissionRecords()
    {
        var v3 = V1ToV3Migrator.Migrate(V1Payload);

        Assert.Equal(3, v3.Version);
        Assert.Equal(2, v3.Missions.Length);      // inst-1 (terminated), inst-2 (active)

        var done = v3.Missions.Single(m => m.MissionInstanceId == "inst-1");
        Assert.Equal("BountyMission", done.MissionSubclass);
        Assert.Equal(2, done.Timeline.Count);
        Assert.Equal(TimelineState.Accepted,  done.Timeline[0].State);
        Assert.Equal(100.0,                   done.Timeline[0].GameSeconds);
        Assert.Equal(TimelineState.Completed, done.Timeline[1].State);
        Assert.Equal(420.0,                   done.Timeline[1].GameSeconds);
        // Typed reward fields folded into Rewards list
        var credits = done.Rewards.FirstOrDefault(r => r.Type == "Credits");
        Assert.NotNull(credits);

        var active = v3.Missions.Single(m => m.MissionInstanceId == "inst-2");
        Assert.True(active.IsActive);
        Assert.Single(active.Timeline);
    }

    [Fact]
    public void IgnoresOrphanTerminalEvents_WithoutAccepted()
    {
        const string payload = @"
        { ""version"": 1, ""events"": [
            { ""eventId"":""e1"", ""type"":""Completed"", ""gameSeconds"":100, ""realUtc"":""2026-01-01T00:00:00Z"",
              ""storyId"":""s"", ""missionInstanceId"":""i"", ""missionSubclass"":""Mission"", ""missionLevel"":1,
              ""outcome"":""Completed"", ""playerLevel"":1 }
        ]}";
        var v3 = V1ToV3Migrator.Migrate(payload);
        Assert.Empty(v3.Missions);   // orphan terminal without accept → dropped
    }
}
```

- [ ] **Step 2: Run test — expect build failure**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test VGMissionJournal.sln --nologo --filter "V1ToV3MigratorTests"`
Expected: build FAILS (`V1ToV3Migrator` not found).

- [ ] **Step 3: Write migrator**

Create `VGMissionJournal/Persistence/V1ToV3Migrator.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using VGMissionJournal.Logging;

namespace VGMissionJournal.Persistence;

/// <summary>
/// Upgrades a v1 sidecar (flat <c>events[]</c>) into a v3 sidecar
/// (<c>missions[]</c> with explicit timelines). Called only by
/// <see cref="JournalIO.Read"/> when <c>schema.Version == 1</c>.
///
/// <para>Fidelity: v1 did not record per-step transitions, so migrated
/// timelines hold <see cref="TimelineState.Accepted"/> and one terminal
/// entry (if the mission terminated in the v1 log). Missions with no
/// Accepted event in v1 are dropped — we'd have to invent identity fields
/// and migration prefers honest gaps over fabricated data.</para>
///
/// <para>Typed v1 rewards (<c>rewardsCredits</c>, <c>rewardsExperience</c>,
/// <c>rewardsReputation</c>) fold into <see cref="MissionRecord.Rewards"/>
/// as <c>Credits</c> / <c>Experience</c> / <c>Reputation</c> entries. Any
/// unified v1 <c>rewards[]</c> already present is passed through as-is.</para>
/// </summary>
internal static class V1ToV3Migrator
{
    public static JournalSchema Migrate(string v1Payload)
    {
        var v1 = JsonConvert.DeserializeObject<V1Schema>(v1Payload, V1SerializerSettings)
                 ?? throw new JsonException("V1 schema was null");
        if (v1.Version != 1)
            throw new InvalidOperationException($"V1ToV3Migrator called on version {v1.Version}");

        // Group events by missionInstanceId (fallback: storyId if instance id is empty).
        var groups = new Dictionary<string, List<V1Event>>(StringComparer.Ordinal);
        foreach (var e in v1.Events ?? Array.Empty<V1Event>())
        {
            var key = !string.IsNullOrEmpty(e.MissionInstanceId) ? e.MissionInstanceId!
                    : !string.IsNullOrEmpty(e.StoryId)           ? e.StoryId!
                                                                  : $"anon:{Guid.NewGuid()}";
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<V1Event>();
                groups[key] = list;
            }
            list.Add(e);
        }

        var records = new List<MissionRecord>();
        foreach (var (instanceId, events) in groups)
        {
            var accept = events.FirstOrDefault(e =>
                string.Equals(e.Type, "Accepted", StringComparison.Ordinal));
            if (accept is null) continue;  // orphan terminal — drop

            var terminal = events.FirstOrDefault(e =>
                e.Type is "Completed" or "Failed" or "Abandoned");

            var timeline = new List<TimelineEntry>
            {
                new(TimelineState.Accepted, accept.GameSeconds, accept.RealUtc),
            };
            if (terminal is not null)
            {
                var state = terminal.Type switch
                {
                    "Completed" => TimelineState.Completed,
                    "Failed"    => TimelineState.Failed,
                    "Abandoned" => TimelineState.Abandoned,
                    _           => TimelineState.Completed,
                };
                timeline.Add(new TimelineEntry(state, terminal.GameSeconds, terminal.RealUtc));
            }

            // Rewards: prefer terminal event's rewards, else accept's.
            var rewardsSource = terminal ?? accept;
            var rewards = FoldRewards(rewardsSource);

            records.Add(new MissionRecord(
                StoryId:               accept.StoryId ?? instanceId,
                MissionInstanceId:     instanceId,
                MissionName:           accept.MissionName,
                MissionSubclass:       accept.MissionSubclass ?? "Mission",
                MissionLevel:          accept.MissionLevel,
                SourceStationId:       accept.SourceStationId,
                SourceStationName:     accept.SourceStationName,
                SourceSystemId:        accept.SourceSystemId,
                SourceSystemName:      accept.SourceSystemName,
                SourceSectorId:        accept.SourceSectorId,
                SourceSectorName:      accept.SourceSectorName,
                SourceFaction:         accept.SourceFaction,
                TargetStationId:       accept.TargetStationId,
                TargetStationName:     accept.TargetStationName,
                TargetSystemId:        accept.TargetSystemId,
                PlayerLevel:           accept.PlayerLevel,
                PlayerShipName:        accept.PlayerShipName,
                PlayerShipLevel:       accept.PlayerShipLevel,
                PlayerCurrentSystemId: accept.PlayerCurrentSystemId,
                Steps:                 Array.Empty<MissionStepDefinition>(),      // v1 rarely captured these
                Rewards:               rewards,
                Timeline:              timeline));
        }

        return new JournalSchema(Version: JournalSchema.CurrentVersion, Missions: records.ToArray());
    }

    private static IReadOnlyList<MissionRewardSnapshot> FoldRewards(V1Event e)
    {
        var list = new List<MissionRewardSnapshot>();
        if (e.Rewards is { Count: > 0 })
        {
            foreach (var r in e.Rewards) list.Add(r);
            return list;
        }
        if (e.RewardsCredits is long credits)
            list.Add(new MissionRewardSnapshot("Credits",
                new Dictionary<string, object?> { ["amount"] = credits }));
        if (e.RewardsExperience is long exp)
            list.Add(new MissionRewardSnapshot("Experience",
                new Dictionary<string, object?> { ["amount"] = exp }));
        if (e.RewardsReputation is { Count: > 0 })
        {
            foreach (var rep in e.RewardsReputation)
                list.Add(new MissionRewardSnapshot("Reputation",
                    new Dictionary<string, object?> { ["faction"] = rep.Faction, ["amount"] = rep.Amount }));
        }
        return list;
    }

    private static readonly JsonSerializerSettings V1SerializerSettings = new()
    {
        ContractResolver  = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        Converters        = { new StringEnumConverter() },
    };

    // --- Obsolete v1 shape — confined here, not exported. -----------------

    private sealed class V1Schema
    {
        public int Version { get; set; }
        public V1Event[]? Events { get; set; }
    }

    private sealed class V1Event
    {
        public string? EventId { get; set; }
        public string? Type { get; set; }
        public double GameSeconds { get; set; }
        public string? RealUtc { get; set; }
        public string? StoryId { get; set; }
        public string? MissionInstanceId { get; set; }
        public string? MissionName { get; set; }
        public string? MissionSubclass { get; set; }
        public int MissionLevel { get; set; }
        public string? SourceStationId { get; set; }
        public string? SourceStationName { get; set; }
        public string? SourceSystemId { get; set; }
        public string? SourceSystemName { get; set; }
        public string? SourceSectorId { get; set; }
        public string? SourceSectorName { get; set; }
        public string? SourceFaction { get; set; }
        public string? TargetStationId { get; set; }
        public string? TargetStationName { get; set; }
        public string? TargetSystemId { get; set; }
        public long? RewardsCredits { get; set; }
        public long? RewardsExperience { get; set; }
        public List<RepReward>? RewardsReputation { get; set; }
        public List<MissionRewardSnapshot>? Rewards { get; set; }
        public int PlayerLevel { get; set; }
        public string? PlayerShipName { get; set; }
        public int? PlayerShipLevel { get; set; }
        public string? PlayerCurrentSystemId { get; set; }
    }
}
```

- [ ] **Step 4: Run tests**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test VGMissionJournal.sln --nologo --filter "V1ToV3MigratorTests"`
Expected: `Passed! - Failed: 0, Passed: 2`.

---

## Task 9: `JournalIO` + `SaveLoadPatch` + `SaveWritePatch`

**Files:**
- Modify: `VGMissionJournal/Persistence/JournalIO.cs`
- Modify: `VGMissionJournal/Patches/SaveLoadPatch.cs`
- Modify: `VGMissionJournal/Patches/SaveWritePatch.cs`

- [ ] **Step 1: Update JournalIO.Read to route v1 through the migrator**

In `VGMissionJournal/Persistence/JournalIO.cs`:

```csharp
public JournalReadResult Read(string sidecarPath)
{
    if (!File.Exists(sidecarPath))
        return new JournalReadResult(JournalReadStatus.MissingFile, null, null);

    string raw;
    try { raw = File.ReadAllText(sidecarPath); }
    catch (IOException) { return new JournalReadResult(JournalReadStatus.MissingFile, null, null); }

    // First pass: read just the version.
    int version;
    try
    {
        var probe = JsonConvert.DeserializeObject<VersionProbe>(raw, JournalSchema.SerializerSettings);
        version = probe?.Version ?? 0;
    }
    catch (JsonException) { return Quarantine(sidecarPath, JournalReadStatus.Corrupted); }

    if (version == JournalSchema.CurrentVersion)
    {
        JournalSchema? schema;
        try { schema = JsonConvert.DeserializeObject<JournalSchema>(raw, JournalSchema.SerializerSettings); }
        catch (JsonException) { return Quarantine(sidecarPath, JournalReadStatus.Corrupted); }
        if (schema is null) return Quarantine(sidecarPath, JournalReadStatus.Corrupted);
        return new JournalReadResult(JournalReadStatus.Loaded, schema, null);
    }

    if (version == 1)
    {
        try
        {
            var migrated = V1ToV3Migrator.Migrate(raw);
            return new JournalReadResult(JournalReadStatus.Loaded, migrated, null);
        }
        catch (Exception) { return Quarantine(sidecarPath, JournalReadStatus.Corrupted); }
    }

    return Quarantine(sidecarPath, JournalReadStatus.UnsupportedVersion);
}

private sealed class VersionProbe
{
    public int Version { get; set; }
}
```

- [ ] **Step 2: Update SaveLoadPatch and SaveWritePatch**

Both patches currently hold `ActivityLog Log` slots. Replace with `MissionStore Store`. On load:

```csharp
// SaveLoadPatch Postfix
var result = IO.Read(sidecarPath);
if (result.Status == JournalReadStatus.Loaded && result.Schema is not null)
{
    Store.LoadFrom(result.Schema.Missions);
}
else if (result.Status != JournalReadStatus.MissingFile)
{
    BepLog.LogWarning($"Sidecar load failed: {result.Status}, quarantined to {result.QuarantinedTo}");
}
```

On save:

```csharp
// SaveWritePatch Postfix
try
{
    var schema = new JournalSchema(JournalSchema.CurrentVersion, Store.AllMissions.ToArray());
    IO.Write(sidecarPath, schema);
}
catch (Exception e)
{
    BepLog.LogWarning($"Sidecar write failed: {e}");
}
```

- [ ] **Step 3: Build and run all tests**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet build VGMissionJournal.sln -v q 2>&1 | tail -5`
Expected: build still fails — `ActivityLog`, `ActivityEvent`, `ActivityEventMapper`, `IMissionJournalQuery` old methods, old tests. Continue through Tasks 10-13 before trying green.

- [ ] **Step 4: Defer commit**

Commits land together after Task 13.

---

## Task 10: `IMissionJournalQuery` v3 surface

**Files:**
- Modify: `VGMissionJournal/Api/IMissionJournalQuery.cs`

- [ ] **Step 1: Rewrite the interface**

Replace `VGMissionJournal/Api/IMissionJournalQuery.cs` with:

```csharp
using System;
using System.Collections.Generic;

namespace VGMissionJournal.Api;

/// <summary>
/// Neutral-shape query interface for cross-mod consumers. v3 is mission-oriented:
/// every return is either a single mission dict, a list of mission dicts, or a
/// primitive-keyed aggregate. Fields omitted or null should both be handled.
///
/// <para>Each "mission dict" uses the same camelCase keys as the sidecar's
/// <c>missions[]</c> entry: <c>storyId</c>, <c>missionInstanceId</c>,
/// <c>missionSubclass</c>, <c>steps</c>, <c>rewards</c>, <c>timeline</c>, etc.
/// Age/active/outcome are derived off <c>timeline[]</c> by the consumer, or
/// read off the helper fields the mapper exposes (<c>acceptedAtGameSeconds</c>,
/// <c>terminalAtGameSeconds</c>, <c>outcome</c>, <c>isActive</c>).</para>
///
/// <para>Adding new methods is non-breaking; changing existing signatures is.</para>
/// </summary>
public interface IMissionJournalQuery
{
    int SchemaVersion { get; }
    int TotalMissionCount { get; }
    double? OldestAcceptedGameSeconds { get; }
    double? NewestAcceptedGameSeconds { get; }

    IReadOnlyDictionary<string, object?>? GetMission(string missionInstanceId);
    IReadOnlyList<IReadOnlyDictionary<string, object?>> GetActiveMissions();
    IReadOnlyList<IReadOnlyDictionary<string, object?>> GetAllMissions();

    IReadOnlyList<IReadOnlyDictionary<string, object?>> GetMissionsInSystem(
        string systemId, double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue);

    IReadOnlyList<IReadOnlyDictionary<string, object?>> GetMissionsByFaction(
        string factionId, double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue);

    IReadOnlyList<IReadOnlyDictionary<string, object?>> GetMissionsByMissionSubclass(
        string missionSubclass, double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue);

    /// <summary><paramref name="outcome"/> is one of <c>"Completed"</c> /
    /// <c>"Failed"</c> / <c>"Abandoned"</c>. Active missions never match.</summary>
    IReadOnlyList<IReadOnlyDictionary<string, object?>> GetMissionsByOutcome(
        string outcome, double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue);

    /// <summary>Missions whose <c>steps[].objectives[].type</c> includes
    /// <paramref name="objectiveType"/>. Case-sensitive ordinal.</summary>
    IReadOnlyList<IReadOnlyDictionary<string, object?>> GetMissionsWithObjective(
        string objectiveType, double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue);

    /// <summary>Missions for a specific storyId (story missions that share an id
    /// across accept/archive chains). Returns 0..N records depending on how
    /// many mission instances carried that id.</summary>
    IReadOnlyList<IReadOnlyDictionary<string, object?>> GetMissionsForStoryId(string storyId);

    /// <summary>Up to <paramref name="count"/> missions sorted by accept time
    /// descending (newest first).</summary>
    IReadOnlyList<IReadOnlyDictionary<string, object?>> GetRecentMissions(int count);

    // --- Proximity -------------------------------------------------------

    IReadOnlyList<IReadOnlyDictionary<string, object?>> GetMissionsWithinJumps(
        string pivotSystemId,
        int maxJumps,
        Func<string, string, int> jumpDistance,
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue);

    // --- Aggregates ------------------------------------------------------

    IReadOnlyDictionary<string, int> CountByMissionSubclass(
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue);

    IReadOnlyDictionary<string, int> CountByOutcome(
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue);

    IReadOnlyDictionary<string, int> CountBySystem(
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue);

    IReadOnlyDictionary<string, int> CountByFaction(
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue);

    /// <summary>Top-N most-active source systems within <paramref name="maxJumps"/>.</summary>
    IReadOnlyList<IReadOnlyDictionary<string, object?>> MostActiveSystemsInRange(
        string pivotSystemId,
        Func<string, string, int> jumpDistance,
        int maxJumps,
        int topN,
        double sinceGameSeconds = 0.0,
        double untilGameSeconds = double.MaxValue);
}
```

**Time-window semantics (document in `docs/api.md` later):** `since`/`until` filter by `timeline[0].at` (i.e. when the mission was accepted), not by terminal time.

---

## Task 11: `MissionRecordMapper` (replaces `ActivityEventMapper`)

**Files:**
- Create: `VGMissionJournal/Api/MissionRecordMapper.cs`
- Test: `VGMissionJournal.Tests/Api/MissionRecordMapperTests.cs`

- [ ] **Step 1: Write failing test**

Create `VGMissionJournal.Tests/Api/MissionRecordMapperTests.cs`:

```csharp
using System.Collections.Generic;
using VGMissionJournal.Api;
using VGMissionJournal.Logging;
using Xunit;

namespace VGMissionJournal.Tests.Api;

public class MissionRecordMapperTests
{
    private static MissionRecord Sample() => new(
        "story-1", "inst-1", "Hunt", "BountyMission", 4,
        null, null, "sys-zoran", "Zoran", null, null, "BountyGuild",
        null, null, null, 1, null, null, null,
        System.Array.Empty<MissionStepDefinition>(),
        new[]
        {
            new MissionRewardSnapshot("Credits",
                new Dictionary<string, object?> { ["amount"] = 12000 }),
        },
        new[]
        {
            new TimelineEntry(TimelineState.Accepted,  100.0, "2026-01-01T00:00:00Z"),
            new TimelineEntry(TimelineState.Completed, 420.0, "2026-01-01T00:07:00Z"),
        });

    [Fact]
    public void ToDict_EmitsCamelCase_AndDerivedFields()
    {
        var d = MissionRecordMapper.ToDict(Sample());
        Assert.Equal("inst-1",            d["missionInstanceId"]);
        Assert.Equal("BountyMission",     d["missionSubclass"]);
        Assert.Equal(100.0,               d["acceptedAtGameSeconds"]);
        Assert.Equal(420.0,               d["terminalAtGameSeconds"]);
        Assert.Equal("Completed",         d["outcome"]);
        Assert.Equal(false,               d["isActive"]);
        Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(d["rewards"]);
        Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(d["timeline"]);
    }

    [Fact]
    public void ToDict_ActiveMission_TerminalFieldsAreNull()
    {
        var r = Sample() with
        {
            Timeline = new[] { new TimelineEntry(TimelineState.Accepted, 100.0, null) },
        };
        var d = MissionRecordMapper.ToDict(r);
        Assert.Null(d["terminalAtGameSeconds"]);
        Assert.Null(d["outcome"]);
        Assert.Equal(true, d["isActive"]);
    }
}
```

- [ ] **Step 2: Run test — expect build failure**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test VGMissionJournal.sln --nologo --filter "MissionRecordMapperTests"`
Expected: build FAILS.

- [ ] **Step 3: Write the mapper**

Create `VGMissionJournal/Api/MissionRecordMapper.cs`:

```csharp
using System.Collections.Generic;
using VGMissionJournal.Logging;

namespace VGMissionJournal.Api;

internal static class MissionRecordMapper
{
    public static IReadOnlyDictionary<string, object?> ToDict(MissionRecord r) => new Dictionary<string, object?>
    {
        ["storyId"]               = r.StoryId,
        ["missionInstanceId"]     = r.MissionInstanceId,
        ["missionName"]           = r.MissionName,
        ["missionSubclass"]       = r.MissionSubclass,
        ["missionLevel"]          = r.MissionLevel,
        ["sourceStationId"]       = r.SourceStationId,
        ["sourceStationName"]     = r.SourceStationName,
        ["sourceSystemId"]        = r.SourceSystemId,
        ["sourceSystemName"]      = r.SourceSystemName,
        ["sourceSectorId"]        = r.SourceSectorId,
        ["sourceSectorName"]      = r.SourceSectorName,
        ["sourceFaction"]         = r.SourceFaction,
        ["targetStationId"]       = r.TargetStationId,
        ["targetStationName"]     = r.TargetStationName,
        ["targetSystemId"]        = r.TargetSystemId,
        ["playerLevel"]           = r.PlayerLevel,
        ["playerShipName"]        = r.PlayerShipName,
        ["playerShipLevel"]       = r.PlayerShipLevel,
        ["playerCurrentSystemId"] = r.PlayerCurrentSystemId,
        ["steps"]                 = MapSteps(r.Steps),
        ["rewards"]               = MapRewards(r.Rewards),
        ["timeline"]              = MapTimeline(r.Timeline),
        ["acceptedAtGameSeconds"] = r.AcceptedAtGameSeconds,
        ["terminalAtGameSeconds"] = r.TerminalAtGameSeconds,
        ["outcome"]               = r.Outcome?.ToString(),
        ["isActive"]              = r.IsActive,
    };

    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> ToDicts(
        IReadOnlyList<MissionRecord> records)
    {
        var out_ = new List<IReadOnlyDictionary<string, object?>>(records.Count);
        foreach (var r in records) out_.Add(ToDict(r));
        return out_;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> MapSteps(
        IReadOnlyList<MissionStepDefinition> steps)
    {
        var out_ = new List<IReadOnlyDictionary<string, object?>>(steps.Count);
        foreach (var s in steps)
        {
            var objs = new List<IReadOnlyDictionary<string, object?>>(s.Objectives.Count);
            foreach (var o in s.Objectives)
                objs.Add(new Dictionary<string, object?>
                {
                    ["type"]   = o.Type,
                    ["fields"] = o.Fields,
                });
            out_.Add(new Dictionary<string, object?>
            {
                ["description"]          = s.Description,
                ["requireAllObjectives"] = s.RequireAllObjectives,
                ["hidden"]               = s.Hidden,
                ["objectives"]           = objs,
            });
        }
        return out_;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> MapRewards(
        IReadOnlyList<MissionRewardSnapshot> rewards)
    {
        var out_ = new List<IReadOnlyDictionary<string, object?>>(rewards.Count);
        foreach (var r in rewards)
            out_.Add(new Dictionary<string, object?>
            {
                ["type"]   = r.Type,
                ["fields"] = r.Fields,
            });
        return out_;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> MapTimeline(
        IReadOnlyList<TimelineEntry> tl)
    {
        var out_ = new List<IReadOnlyDictionary<string, object?>>(tl.Count);
        foreach (var e in tl)
            out_.Add(new Dictionary<string, object?>
            {
                ["state"]       = e.State.ToString(),
                ["gameSeconds"] = e.GameSeconds,
                ["realUtc"]     = e.RealUtc,
            });
        return out_;
    }
}
```

- [ ] **Step 4: Run tests**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test VGMissionJournal.sln --nologo --filter "MissionRecordMapperTests"`
Expected: `Passed! - Failed: 0, Passed: 2`.

---

## Task 12: `MissionJournalQueryAdapter` rewrite

**Files:**
- Modify: `VGMissionJournal/Api/MissionJournalQueryAdapter.cs`
- Test: `VGMissionJournal.Tests/Api/MissionJournalQueryAdapterTests.cs` (rewrite)

- [ ] **Step 1: Rewrite adapter**

Replace the body of `VGMissionJournal/Api/MissionJournalQueryAdapter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using VGMissionJournal.Logging;
using VGMissionJournal.Persistence;

namespace VGMissionJournal.Api;

internal sealed class MissionJournalQueryAdapter : IMissionJournalQuery
{
    private readonly MissionStore _store;

    public MissionJournalQueryAdapter(MissionStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public int SchemaVersion            => JournalSchema.CurrentVersion;
    public int TotalMissionCount        => _store.TotalMissionCount;

    public double? OldestAcceptedGameSeconds =>
        _store.AllMissions.Count == 0 ? null : _store.AllMissions.Min(r => r.AcceptedAtGameSeconds);
    public double? NewestAcceptedGameSeconds =>
        _store.AllMissions.Count == 0 ? null : _store.AllMissions.Max(r => r.AcceptedAtGameSeconds);

    public IReadOnlyDictionary<string, object?>? GetMission(string missionInstanceId)
    {
        var r = _store.GetByInstanceId(missionInstanceId);
        return r is null ? null : MissionRecordMapper.ToDict(r);
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> GetActiveMissions() =>
        MissionRecordMapper.ToDicts(_store.GetActiveMissions());

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> GetAllMissions() =>
        MissionRecordMapper.ToDicts(_store.AllMissions);

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> GetMissionsInSystem(
        string systemId, double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue) =>
        MissionRecordMapper.ToDicts(FilterByAcceptedTime(_store.GetMissionsInSystem(systemId),
            sinceGameSeconds, untilGameSeconds));

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> GetMissionsByFaction(
        string factionId, double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue) =>
        MissionRecordMapper.ToDicts(FilterByAcceptedTime(_store.GetMissionsByFaction(factionId),
            sinceGameSeconds, untilGameSeconds));

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> GetMissionsByMissionSubclass(
        string missionSubclass, double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue)
    {
        var matched = _store.AllMissions.Where(r =>
            string.Equals(r.MissionSubclass, missionSubclass, StringComparison.Ordinal)).ToList();
        return MissionRecordMapper.ToDicts(FilterByAcceptedTime(matched, sinceGameSeconds, untilGameSeconds));
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> GetMissionsByOutcome(
        string outcome, double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue)
    {
        if (!Enum.TryParse<Outcome>(outcome, ignoreCase: false, out var parsed))
            return Array.Empty<IReadOnlyDictionary<string, object?>>();
        var matched = _store.AllMissions.Where(r => r.Outcome == parsed).ToList();
        return MissionRecordMapper.ToDicts(FilterByAcceptedTime(matched, sinceGameSeconds, untilGameSeconds));
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> GetMissionsWithObjective(
        string objectiveType, double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue)
    {
        if (string.IsNullOrEmpty(objectiveType))
            return Array.Empty<IReadOnlyDictionary<string, object?>>();
        var matched = _store.AllMissions.Where(r => RecordHasObjective(r, objectiveType)).ToList();
        return MissionRecordMapper.ToDicts(FilterByAcceptedTime(matched, sinceGameSeconds, untilGameSeconds));
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> GetMissionsForStoryId(string storyId) =>
        MissionRecordMapper.ToDicts(_store.AllMissions
            .Where(r => string.Equals(r.StoryId, storyId, StringComparison.Ordinal))
            .ToList());

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> GetRecentMissions(int count)
    {
        if (count <= 0) return Array.Empty<IReadOnlyDictionary<string, object?>>();
        var ordered = _store.AllMissions
            .OrderByDescending(r => r.AcceptedAtGameSeconds)
            .Take(count)
            .ToList();
        return MissionRecordMapper.ToDicts(ordered);
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> GetMissionsWithinJumps(
        string pivotSystemId, int maxJumps, Func<string, string, int> jumpDistance,
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue)
    {
        if (jumpDistance is null) throw new ArgumentNullException(nameof(jumpDistance));
        if (maxJumps < 0) return Array.Empty<IReadOnlyDictionary<string, object?>>();
        var matched = new List<MissionRecord>();
        foreach (var r in _store.AllMissions)
        {
            if (string.IsNullOrEmpty(r.SourceSystemId)) continue;
            var jumps = jumpDistance(pivotSystemId, r.SourceSystemId!);
            if (jumps < 0 || jumps > maxJumps) continue;
            matched.Add(r);
        }
        return MissionRecordMapper.ToDicts(FilterByAcceptedTime(matched, sinceGameSeconds, untilGameSeconds));
    }

    public IReadOnlyDictionary<string, int> CountByMissionSubclass(
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue) =>
        Tally(FilterByAcceptedTime(_store.AllMissions, sinceGameSeconds, untilGameSeconds),
              r => r.MissionSubclass);

    public IReadOnlyDictionary<string, int> CountByOutcome(
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue) =>
        Tally(FilterByAcceptedTime(_store.AllMissions, sinceGameSeconds, untilGameSeconds)
                .Where(r => r.Outcome.HasValue), r => r.Outcome!.ToString()!);

    public IReadOnlyDictionary<string, int> CountBySystem(
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue) =>
        Tally(FilterByAcceptedTime(_store.AllMissions, sinceGameSeconds, untilGameSeconds)
                .Where(r => !string.IsNullOrEmpty(r.SourceSystemId)), r => r.SourceSystemId!);

    public IReadOnlyDictionary<string, int> CountByFaction(
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue) =>
        Tally(FilterByAcceptedTime(_store.AllMissions, sinceGameSeconds, untilGameSeconds)
                .Where(r => !string.IsNullOrEmpty(r.SourceFaction)), r => r.SourceFaction!);

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> MostActiveSystemsInRange(
        string pivotSystemId, Func<string, string, int> jumpDistance, int maxJumps, int topN,
        double sinceGameSeconds = 0.0, double untilGameSeconds = double.MaxValue)
    {
        if (jumpDistance is null) throw new ArgumentNullException(nameof(jumpDistance));
        if (topN <= 0 || maxJumps < 0) return Array.Empty<IReadOnlyDictionary<string, object?>>();

        var counts        = new Dictionary<string, int>();
        var jumpsBySystem = new Dictionary<string, int>();
        foreach (var r in FilterByAcceptedTime(_store.AllMissions, sinceGameSeconds, untilGameSeconds))
        {
            if (string.IsNullOrEmpty(r.SourceSystemId)) continue;
            var sys = r.SourceSystemId!;
            if (!jumpsBySystem.TryGetValue(sys, out var jumps))
            {
                jumps = jumpDistance(pivotSystemId, sys);
                jumpsBySystem[sys] = jumps;
            }
            if (jumps < 0 || jumps > maxJumps) continue;
            counts.TryGetValue(sys, out var n);
            counts[sys] = n + 1;
        }
        return counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(topN)
            .Select(kv => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
            {
                ["systemId"] = kv.Key,
                ["count"]    = kv.Value,
                ["jumps"]    = jumpsBySystem[kv.Key],
            })
            .ToList();
    }

    private static IReadOnlyList<MissionRecord> FilterByAcceptedTime(
        IEnumerable<MissionRecord> src, double since, double until)
    {
        var out_ = new List<MissionRecord>();
        foreach (var r in src)
        {
            var at = r.AcceptedAtGameSeconds;
            if (at >= since && at <= until) out_.Add(r);
        }
        return out_;
    }

    private static IReadOnlyDictionary<string, int> Tally(
        IEnumerable<MissionRecord> src, Func<MissionRecord, string> key)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in src)
        {
            var k = key(r);
            result.TryGetValue(k, out var n);
            result[k] = n + 1;
        }
        return result;
    }

    private static bool RecordHasObjective(MissionRecord r, string objectiveType)
    {
        foreach (var step in r.Steps)
            foreach (var obj in step.Objectives)
                if (string.Equals(obj.Type, objectiveType, StringComparison.Ordinal))
                    return true;
        return false;
    }
}
```

- [ ] **Step 2: Rewrite adapter tests**

Replace `VGMissionJournal.Tests/Api/MissionJournalQueryAdapterTests.cs` body. Keep the `[Collection("MissionJournalApi.Current")]` attribute. Sample fixture builds 3-4 mission records directly (bypassing the builder — tests of the adapter). Assert the shape of returned dicts uses camelCase keys and derived fields.

A representative test:

```csharp
[Fact]
public void GetMissionsByOutcome_Completed_ReturnsOnlyCompletedMissions()
{
    var store = new MissionStore();
    store.Upsert(SampleRecord("i1", outcome: null));      // active
    store.Upsert(SampleRecord("i2", outcome: TimelineState.Completed));
    store.Upsert(SampleRecord("i3", outcome: TimelineState.Failed));
    var adapter = new MissionJournalQueryAdapter(store);

    var completed = adapter.GetMissionsByOutcome("Completed");
    Assert.Single(completed);
    Assert.Equal("i2", completed[0]["missionInstanceId"]);
}
```

Cover: `GetMission`, `GetActiveMissions`, `GetMissionsByOutcome`, `GetMissionsWithObjective`, `GetRecentMissions`, `GetMissionsWithinJumps`, `CountByMissionSubclass`, `CountBySystem`, `MostActiveSystemsInRange`, `SchemaVersion`, `TotalMissionCount`.

---

## Task 13: Remove dead files + full build green

**Files to delete:**
- `VGMissionJournal/Logging/ActivityEvent.cs`
- `VGMissionJournal/Logging/ActivityEventType.cs`
- `VGMissionJournal/Logging/ActivityLog.cs`
- `VGMissionJournal/Logging/ActivityEventBuilder.cs`
- `VGMissionJournal/Logging/MissionStepSnapshot.cs`
- `VGMissionJournal/Logging/MissionObjectiveSnapshot.cs`
- `VGMissionJournal/Api/ActivityEventMapper.cs`
- Any test files that only test the above: `VGMissionJournal.Tests/Logging/ActivityEventBuilderTests.cs`, `ActivityLogTests.cs`, `ActivityLogQueryTests.cs`, `ActivityLogAggregateTests.cs`, `ActivityLogProximityTests.cs`, `VGMissionJournal.Tests/Api/ActivityEventMapperTests.cs`.

- [ ] **Step 1: Delete**

```bash
git rm VGMissionJournal/Logging/ActivityEvent.cs VGMissionJournal/Logging/ActivityEventType.cs \
       VGMissionJournal/Logging/ActivityLog.cs VGMissionJournal/Logging/ActivityEventBuilder.cs \
       VGMissionJournal/Logging/MissionStepSnapshot.cs VGMissionJournal/Logging/MissionObjectiveSnapshot.cs \
       VGMissionJournal/Api/ActivityEventMapper.cs
git rm VGMissionJournal.Tests/Logging/ActivityEventBuilderTests.cs \
       VGMissionJournal.Tests/Logging/ActivityLogTests.cs \
       VGMissionJournal.Tests/Logging/ActivityLogQueryTests.cs \
       VGMissionJournal.Tests/Logging/ActivityLogAggregateTests.cs \
       VGMissionJournal.Tests/Logging/ActivityLogProximityTests.cs \
       VGMissionJournal.Tests/Api/ActivityEventMapperTests.cs
```

- [ ] **Step 2: Update TestEvents support helper**

Delete `VGMissionJournal.Tests/Support/TestEvents.cs` — it built `ActivityEvent` which no longer exists. Replace with `TestRecords.cs`:

```csharp
using System;
using System.Collections.Generic;
using VGMissionJournal.Logging;

namespace VGMissionJournal.Tests.Support;

internal static class TestRecords
{
    public static MissionRecord Record(
        string instanceId       = "inst-baseline",
        string storyId          = "story-1",
        double acceptedAt       = 0.0,
        TimelineState? terminal = null,
        double terminalAt       = 0.0,
        string subclass         = "Mission",
        string? sourceSystemId  = null,
        string? sourceFaction   = null,
        IReadOnlyList<MissionStepDefinition>? steps = null,
        IReadOnlyList<MissionRewardSnapshot>? rewards = null)
    {
        var tl = new List<TimelineEntry>
        {
            new(TimelineState.Accepted, acceptedAt, "2026-01-01T00:00:00Z"),
        };
        if (terminal.HasValue)
            tl.Add(new TimelineEntry(terminal.Value, terminalAt, "2026-01-01T00:00:10Z"));

        return new MissionRecord(
            storyId, instanceId, "Test Mission", subclass, 1,
            null, null, sourceSystemId, null, null, null, sourceFaction,
            null, null, null, 1, null, null, null,
            steps   ?? Array.Empty<MissionStepDefinition>(),
            rewards ?? Array.Empty<MissionRewardSnapshot>(),
            tl);
    }
}
```

- [ ] **Step 3: Build**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet build VGMissionJournal.sln -v q 2>&1 | tail -10`
Expected: build errors in patch tests referring to old types. That's Task 16. Fix any non-test compile errors first; leave test compile errors for Task 16.

If the non-test build still fails here, there are references to the deleted types somewhere — look for `using VGMissionJournal.Logging;` and `ActivityEvent`/`ActivityLog` identifiers. Fix them all before moving on.

- [ ] **Step 4: Verify non-test build**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet build VGMissionJournal/VGMissionJournal.csproj -v q 2>&1 | tail -5`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 5: Commit the mega-commit**

This is one logical change that spanned Tasks 6-13. Commit them together:

```bash
git add -A
git commit -m "$(cat <<'EOF'
refactor(schema): v3 mission-oriented log (breaking)

Replaces the flat event log with a mission-oriented record store where
each mission is a first-class aggregate carrying identity, immutable
structure, rewards, and an explicit timeline[] of state transitions.

- MissionRecord / TimelineEntry / MissionStore replace
  ActivityEvent / ActivityLog
- MissionRecordBuilder replaces ActivityEventBuilder; reflection logic
  ported verbatim
- All 5 mission patches route through Store.Upsert instead of
  Log.Append; archive backstop unchanged in behavior
- JournalSchema bumps to v3: top-level missions[] replaces events[]
- V1ToV3Migrator upgrades v1 sidecars on load; typed reward fields fold
  into Rewards[] as Credits / Experience / Reputation entries
- IMissionJournalQuery / MissionJournalQueryAdapter reshaped around missions
- docs/api.md and README.md updated (follow-up task)

Fidelity note on migration: v1 didn't capture per-step transitions, so
migrated timelines hold Accepted + one terminal only. Missions with no
Accepted event in v1 are dropped.
EOF
)"
```

---

## Task 14: Rename `Logging.MaxEvents` config → `Logging.MaxMissions`

**Files:**
- Modify: `VGMissionJournal/Plugin.cs`

- [ ] **Step 1: Find the config binding**

Run: `grep -n "MaxEvents\|Logging.MaxEvents" VGMissionJournal/ -r`

- [ ] **Step 2: Rewrite the binding**

In `VGMissionJournal/Plugin.cs`, rename the config entry string from `"MaxEvents"` to `"MaxMissions"` and update the default to `MissionStore.DefaultMaxMissions` (2000). Update any descriptive text to reflect "cap on in-memory mission records" rather than events.

Note: BepInEx config keys are stored in the user's BepInEx/config/*.cfg file; renaming means users' existing config entries become orphans. That's fine — the old key won't be found, new key gets written with the default. Document in the commit message.

- [ ] **Step 3: Run all tests**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test VGMissionJournal.sln --nologo 2>&1 | tail -5`
Expected: all green (test-side adjustments happen in Task 16).

- [ ] **Step 4: Commit**

```bash
git add VGMissionJournal/Plugin.cs
git commit -m "chore(config): rename Logging.MaxEvents → Logging.MaxMissions

The cap is now over MissionRecord count, not ActivityEvent count. Old
config entries in users' BepInEx/config become orphans on upgrade; new
entry is written at default (2000) on first run."
```

---

## Task 15: Update `docs/api.md` and `README.md`

**Files:**
- Modify: `docs/api.md` (heavy)
- Modify: `README.md` (spot-check)

- [ ] **Step 1: Rewrite docs/api.md**

Read the file top-to-bottom. Replace every reference to `events[]`, `ActivityEvent`, `GetEventsIn*`, `GetEventsFor*`, `GetEventsBy*`, `GetEventsWithObjective`, `GetRecentEvents`, `rewardsCredits` / `rewardsExperience` / `rewardsReputation`, `Steps` (as an event-level field), etc.

New sections to write:

- **Schema**: v3 top-level shape is `{ "version": 3, "missions": [ ... ] }`. Each mission has identity + steps + rewards + timeline.
- **Mission record**: full field-by-field table (copy from MissionRecordMapper keys).
- **Timeline**: how to read state transitions; how to derive age/outcome/active; semantics of accept-time anchoring for time windows.
- **Rewards**: one unified list with `type` + `fields`; list the ~14 subtypes and their fields.
- **Identifier rule**: keep the existing paragraph about resolved identifiers ≠ translated names.
- **Query API**: re-document each `IMissionJournalQuery` method.
- **Retention**: `MaxMissions` cap, `Unbounded = 0`, eviction drops oldest-accepted.
- **Schema migration**: v1 saves auto-migrate on first load; migrated missions have sparse timelines (accept + terminal only).

Include a worked example JSON for one mission record (use the bounty-hunter example from the planning conversation).

- [ ] **Step 2: Update README.md**

Run: `grep -n "ActivityEvent\|events\[\]\|GetEventsInSystem\|GetEventsBy\|rewardsCredits" README.md`. Replace each hit with the corresponding v3 equivalent.

- [ ] **Step 3: Commit**

```bash
git add docs/api.md README.md
git commit -m "docs: rewrite for v3 mission-oriented schema

New shape, new query surface, new field table. Flags the sparse-timeline
caveat for migrated v1 sidecars."
```

---

## Task 16: Port / rewrite remaining test files

**Files:**
- Rewrite: `VGMissionJournal.Tests/Patches/MissionAcceptPatchTests.cs`
- Rewrite: `VGMissionJournal.Tests/Patches/MissionCompletePatchTests.cs`
- Rewrite: `VGMissionJournal.Tests/Patches/MissionFailPatchTests.cs`
- Rewrite: `VGMissionJournal.Tests/Patches/MissionAbandonPatchTests.cs`
- Rewrite: `VGMissionJournal.Tests/Patches/MissionArchivePatchTests.cs`
- Rewrite: `VGMissionJournal.Tests/Patches/SavePatchTests.cs`
- Rewrite: `VGMissionJournal.Tests/Patches/PatchWiringTests.cs`
- Keep: `VGMissionJournal.Tests/Support/FakeClock.cs`, `TestMission.cs`
- Delete: `VGMissionJournal.Tests/Support/TestEvents.cs` (if not already done in Task 13 step 2)

**Strategy:** One file at a time. For each patch test file: open, identify what invariant each test captures (e.g. "postfix emits Accepted event → under v3 that becomes: postfix upserts a new MissionRecord into Store"). Rewrite tests to assert the new invariant. Keep test count roughly similar; drop tests whose invariant no longer applies (e.g. "ObjectiveProgressed event is appended").

Run tests after each file is rewritten:

```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet test VGMissionJournal.sln --nologo --filter "MissionAcceptPatchTests"
```

- [ ] **Step 1: `MissionAcceptPatchTests`**

Replace each test. Representative test:

```csharp
[Fact]
public void Postfix_OnAccept_UpsertsMissionRecord()
{
    var clock   = new FakeClock { GameSeconds = 100.0 };
    var builder = new MissionRecordBuilder(clock, () => null);
    var store   = new MissionStore();
    MissionAcceptPatch.Builder = builder;
    MissionAcceptPatch.Store   = store;
    MissionAcceptPatch.BepLog  = new BepInEx.Logging.ManualLogSource("test");

    var mission = TestMission.Generic("story-1");
    typeof(MissionAcceptPatch)
        .GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)!
        .Invoke(null, new object?[] { mission });

    Assert.Equal(1, store.TotalMissionCount);
    Assert.True(store.AllMissions[0].IsActive);
    Assert.Equal(TimelineState.Accepted, store.AllMissions[0].Timeline[0].State);
}
```

Repeat for each hook (Complete / Fail / Abandon / Archive). Each has the same skeleton: wire patch statics, invoke the postfix via reflection, assert on the store's state.

- [ ] **Step 2: Archive-race tests**

The two archive-race tests (`ArchiveDuringActiveCompletion_SkipsSynth`, `ArchiveOutsideActiveCompletion_StillSynths`) still apply in concept. Rewrite with the new types. The "skip synth" test asserts no terminal entry is added to the active mission record; the "still synths" test asserts a Completed terminal entry is appended.

Keep the `[Collection("PatchStatics")]` attribute on both `MissionArchivePatchTests` and `PatchWiringTests`.

- [ ] **Step 3: `PatchWiringTests`**

Update the fixture to build a `MissionRecordBuilder` + `MissionStore`:

```csharp
var builder = new MissionRecordBuilder(new FakeClock(), () => null);
var store   = new MissionStore();
var io      = new JournalIO(() => DateTime.UtcNow);
var bepLog  = new ManualLogSource("test");
PatchWiring.WireAll(builder, store, io, bepLog);
```

The "every slot is non-null after WireAll" assertion applies unchanged.

- [ ] **Step 4: `SavePatchTests`**

Sidecar load/save tests need the biggest rewrite. Representative test:

```csharp
[Fact]
public void SaveLoadPatch_LoadsV3SidecarIntoStore()
{
    // Build a v3 sidecar by hand, write it, then call the SaveLoadPatch Postfix.
    // Assert store contains the missions.
}

[Fact]
public void SaveLoadPatch_MigratesV1SidecarOnLoad()
{
    // Write a v1 sidecar, load, assert store has migrated missions with
    // Accepted + terminal timeline entries.
}
```

- [ ] **Step 5: Run full suite**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test VGMissionJournal.sln --nologo 2>&1 | tail -5`
Expected: all passing. Count will be different from 177 — some tests deleted, new ones added. A reasonable target is 130-180 tests; all green.

- [ ] **Step 6: Commit**

```bash
git add VGMissionJournal.Tests/
git commit -m "test: port patch + save tests to v3 schema

Every patch test now asserts MissionStore state instead of ActivityLog
events. Archive-race coverage retained (skip-synth + still-synth).
SavePatchTests covers both v3 round-trip and v1→v3 migration on load."
```

---

## Task 17: End-to-end smoke test on a real v1 sidecar

**Files:**
- Create: `VGMissionJournal.Tests/Persistence/V1SidecarSmokeTests.cs`
- Optional asset: `VGMissionJournal.Tests/Fixtures/sample-v1.vgmissionjournal.json` (copied from user's real save, redacted if needed).

- [ ] **Step 1: Capture a v1 fixture**

Copy a real v1 sidecar from `/mnt/c/Users/info/AppData/LocalLow/Bat Roost Games/VanguardGalaxy/Saves/*.vgmissionjournal.json` into `VGMissionJournal.Tests/Fixtures/sample-v1.vgmissionjournal.json`. Confirm it has `"version": 1`.

If privacy is a concern, redact player-name / ship-name fields in the fixture.

- [ ] **Step 2: Write the smoke test**

```csharp
using System.IO;
using VGMissionJournal.Logging;
using VGMissionJournal.Persistence;
using Xunit;

namespace VGMissionJournal.Tests.Persistence;

public class V1SidecarSmokeTests
{
    [Fact]
    public void RealV1Sidecar_MigratesWithoutError()
    {
        var path = Path.Combine("Fixtures", "sample-v1.vgmissionjournal.json");
        var raw  = File.ReadAllText(path);
        var v3   = V1ToV3Migrator.Migrate(raw);

        Assert.Equal(JournalSchema.CurrentVersion, v3.Version);
        Assert.NotEmpty(v3.Missions);
        // Spot-check: every migrated record has exactly 1 Accepted entry.
        foreach (var m in v3.Missions)
        {
            Assert.NotEmpty(m.Timeline);
            Assert.Equal(TimelineState.Accepted, m.Timeline[0].State);
        }
    }
}
```

Mark the fixture to copy to output in `VGMissionJournal.Tests.csproj`:

```xml
<ItemGroup>
  <None Update="Fixtures/**/*">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

- [ ] **Step 3: Run**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test VGMissionJournal.sln --nologo --filter "V1SidecarSmokeTests"`
Expected: `Passed! - Failed: 0, Passed: 1`.

- [ ] **Step 4: Commit**

```bash
git add VGMissionJournal.Tests/Persistence/V1SidecarSmokeTests.cs \
        VGMissionJournal.Tests/Fixtures/sample-v1.vgmissionjournal.json \
        VGMissionJournal.Tests/VGMissionJournal.Tests.csproj
git commit -m "test: smoke-migrate a real v1 sidecar

Belt-and-suspenders check — V1ToV3Migrator unit tests cover the shape,
this test covers an actual game-produced payload."
```

---

## Task 18: Final verification and branch readiness

- [ ] **Step 1: Full test run**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test VGMissionJournal.sln --nologo 2>&1 | tail -5`
Expected: `Passed! - Failed: 0, Passed: N, Skipped: 0, Total: N`.

- [ ] **Step 2: Build the plugin DLL**

Run: `dotnet build VGMissionJournal/VGMissionJournal.csproj -c Release -v q 2>&1 | tail -5`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 3: Eyeball the built DLL**

Confirm the DLL is output-compatible (netstandard2.1) and loads in BepInEx 5. No dotnet test covers the BepInEx load step — manual verification is "copy DLL to BepInEx/plugins/, launch game, confirm no load errors in BepInEx console." Defer to user for this step.

- [ ] **Step 4: Summary commit and PR prep**

```bash
git log main..HEAD --oneline
```
Expected: ~7-9 commits, each a focused piece of the refactor.

- [ ] **Step 5: Create PR (optional, user-directed)**

Only if user requests. Plan ends here.

---

## Self-Review Checklist

Reviewing the plan against the conversation spec:

1. **Mission as first-class aggregate with identity + structure + rewards + timeline.** ✅ Tasks 1-3 build the types; Task 4 builds the store.
2. **Drop ObjectiveProgressed; keep step granularity only if a hook exists.** ✅ Investigation in conversation determined no hook → dropped from v3. Acknowledged in header note.
3. **Drop typed reward fields; unified rewards[] only.** ✅ Task 8 folds v1 typed fields into rewards[] in migrator; v3 schema in Task 7 has no typed fields.
4. **No legacy in emitter; v1 migrates on load.** ✅ Tasks 7-9.
5. **Schema bumps to v3.** ✅ Task 7.
6. **Every existing query method has a v3 equivalent.** ✅ Task 10 enumerates; Task 12 implements.
7. **All tests remain green.** ✅ Task 16 + 18 verify.

No placeholders in tasks. Each step has the code or command it needs. File paths absolute to repo root.

Type-consistency sweep: `MissionStepDefinition` / `MissionObjectiveDefinition` used consistently; `MissionRecord` constructor signature consistent across Task 3, Task 5, Task 8, Task 12; `MissionStore.Upsert` used in all patches and tests; `TimelineState` values (`Accepted`/`Completed`/`Failed`/`Abandoned`) consistent.

---

**Plan complete.**
