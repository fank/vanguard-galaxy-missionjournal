using System;
using System.Collections;
using System.Collections.Generic;
using VGMissionLog.Api;
using VGMissionLog.Logging;
using VGMissionLog.Persistence;
using VGMissionLog.Tests.Support;
using Xunit;

namespace VGMissionLog.Tests.Api;

/// <summary>
/// Simulates a reflection-only consumer (spec R4.3). Every type lookup
/// goes through Type.GetType(string) and every method / property
/// invocation uses MethodInfo / PropertyInfo. We don't touch any
/// VGMissionLog type by direct name in the assertions below (only in
/// setup to seed the store) — if a consumer mod does the same, it can
/// interop without a hard dependency on VGMissionLog.dll.
///
/// <para>Returns are now typed <c>MissionRecord</c> aggregates instead
/// of dicts. Reflection consumers read fields via PropertyInfo; the cost
/// is identical to indexing a dict and the failure mode (missing
/// property) is clearer.</para>
/// </summary>
[Collection("MissionLogApi.Current")]
public class ReflectionProbeTests : IDisposable
{
    private readonly MissionStore _store;

    public ReflectionProbeTests()
    {
        _store = new MissionStore();
        _store.Upsert(TestRecords.Record(
            instanceId: "inst-1", storyId: "m-probe", acceptedAt: 42.0,
            subclass: "BountyMission",
            sourceSystemId: "sys-zoran", sourceFaction: "BountyGuild"));
        MissionLogApi.Current = new MissionLogQueryAdapter(_store);
    }

    public void Dispose() => MissionLogApi.Current = null;

    [Fact]
    public void FacadeType_ResolvesViaAssemblyQualifiedName()
    {
        var facadeType = Type.GetType("VGMissionLog.Api.MissionLogApi, VGMissionLog");
        Assert.NotNull(facadeType);
        Assert.True(facadeType!.IsAbstract && facadeType.IsSealed,
            "MissionLogApi should be a static class (abstract + sealed).");
    }

    [Fact]
    public void CurrentProperty_ResolvesAndReturnsLiveHandle()
    {
        var facadeType = Type.GetType("VGMissionLog.Api.MissionLogApi, VGMissionLog")!;
        var currentProp = facadeType.GetProperty("Current");
        Assert.NotNull(currentProp);

        var current = currentProp!.GetValue(null);
        Assert.NotNull(current);
    }

    [Fact]
    public void SchemaVersion_ReadableViaReflection_IsInt()
    {
        var current = GetCurrentViaReflection()!;

        var schemaProp = current.GetType().GetProperty("SchemaVersion");
        Assert.NotNull(schemaProp);

        var value = schemaProp!.GetValue(current);
        Assert.IsType<int>(value);
        Assert.Equal(LogSchema.CurrentVersion, value);
    }

    [Fact]
    public void GetMissionsInSystem_ReturnsListOfMissionRecord_ViaReflection()
    {
        var current = GetCurrentViaReflection()!;
        var method = current.GetType().GetMethod(
            "GetMissionsInSystem", new[] { typeof(string), typeof(double), typeof(double) });
        Assert.NotNull(method);

        var result = method!.Invoke(current, new object[] { "sys-zoran", 0.0, double.MaxValue });
        Assert.NotNull(result);

        // Iterate without casting to any VGMissionLog type — properties
        // are read off each element via PropertyInfo.
        var list = (IEnumerable)result!;
        object? first = null;
        foreach (var item in list) { first = item; break; }
        Assert.NotNull(first);

        var recType = first!.GetType();
        Assert.Equal("inst-1",        recType.GetProperty("MissionInstanceId")!.GetValue(first));
        Assert.Equal("m-probe",       recType.GetProperty("StoryId")!.GetValue(first));
        Assert.Equal("BountyMission", recType.GetProperty("MissionSubclass")!.GetValue(first));
        Assert.Equal("sys-zoran",     recType.GetProperty("SourceSystemId")!.GetValue(first));
        Assert.Equal(42.0,            recType.GetProperty("AcceptedAtGameSeconds")!.GetValue(first));
    }

    [Fact]
    public void CountByMissionSubclass_ReturnsStringToInt_ViaReflection()
    {
        var current = GetCurrentViaReflection()!;
        var method  = current.GetType().GetMethod(
            "CountByMissionSubclass", new[] { typeof(double), typeof(double) });

        var result = method!.Invoke(current, new object[] { 0.0, double.MaxValue });
        var dict   = (IReadOnlyDictionary<string, int>)result!;

        Assert.Equal(1, dict["BountyMission"]);
    }

    // --- helpers ----------------------------------------------------------

    private static object? GetCurrentViaReflection() =>
        Type.GetType("VGMissionLog.Api.MissionLogApi, VGMissionLog")!
            .GetProperty("Current")!
            .GetValue(null);
}
