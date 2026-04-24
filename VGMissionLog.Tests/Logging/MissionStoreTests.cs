using System.Linq;
using VGMissionLog.Logging;
using Xunit;

namespace VGMissionLog.Tests.Logging;

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

    [Fact]
    public void GetMissionsByFaction_FiltersByFaction()
    {
        var store = new MissionStore();
        store.Upsert(Record("i1", "s1", 10.0, sourceFaction: "BountyGuild"));
        store.Upsert(Record("i2", "s2", 20.0, sourceFaction: "Police"));
        store.Upsert(Record("i3", "s3", 30.0, sourceFaction: "BountyGuild"));
        Assert.Equal(new[] { "i1", "i3" },
                     store.GetMissionsByFaction("BountyGuild").Select(r => r.MissionInstanceId));
    }

    [Fact]
    public void Upsert_Replace_PreservesInsertionPosition()
    {
        // The XML doc guarantees: "the insertion position is preserved so
        // iteration order stays stable". Pin that invariant.
        var store = new MissionStore();
        store.Upsert(Record("i1", "s1", 10.0));
        store.Upsert(Record("i2", "s2", 20.0));
        store.Upsert(Record("i3", "s3", 30.0));

        // Update i2 — add a terminal entry.
        store.Upsert(Record("i2", "s2", 20.0,
            afterAccept: new TimelineEntry(TimelineState.Completed, 25.0, null)));

        Assert.Equal(new[] { "i1", "i2", "i3" },
                     store.AllMissions.Select(r => r.MissionInstanceId));
        Assert.False(store.GetByInstanceId("i2")!.IsActive);   // replacement took effect
    }

    [Fact]
    public void LoadFrom_ReplacesContents_AndCapsToMaxMissions()
    {
        var store = new MissionStore(maxMissions: 2);
        store.Upsert(Record("old1", "s", 1.0));
        store.Upsert(Record("old2", "s", 2.0));

        store.LoadFrom(new[]
        {
            Record("i1", "s1", 10.0),
            Record("i2", "s2", 20.0),
            Record("i3", "s3", 30.0),   // exceeds cap — should be trimmed from front
        });

        Assert.Equal(2, store.TotalMissionCount);
        Assert.Equal(new[] { "i2", "i3" },
                     store.AllMissions.Select(r => r.MissionInstanceId));
        Assert.Null(store.GetByInstanceId("old1"));
        Assert.Null(store.GetByInstanceId("i1"));
    }

    [Fact]
    public void Upsert_NullRecord_Throws()
    {
        var store = new MissionStore();
        Assert.Throws<System.ArgumentNullException>(() => store.Upsert(null!));
    }
}
