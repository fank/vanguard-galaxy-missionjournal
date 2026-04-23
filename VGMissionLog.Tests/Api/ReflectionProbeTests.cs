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
/// setup to seed the log) — if a consumer mod does the same, it can
/// interop without a hard dependency on VGMissionLog.dll.
/// </summary>
[Collection("MissionLogApi.Current")]
public class ReflectionProbeTests : IDisposable
{
    private readonly ActivityLog _log;

    public ReflectionProbeTests()
    {
        _log = new ActivityLog();
        _log.Append(TestEvents.Baseline(
            eventId: "evt-1", storyId: "m-probe", gameSeconds: 42.0,
            type: ActivityEventType.Accepted, missionType: MissionType.Bounty,
            sourceSystemId: "sys-zoran", sourceFaction: "BountyGuild"));
        MissionLogApi.Current = new MissionLogQueryAdapter(_log);
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
    public void GetEventsInSystem_ReturnsListOfPrimitiveDict_ViaReflection()
    {
        var current = GetCurrentViaReflection()!;
        var method = current.GetType().GetMethod(
            "GetEventsInSystem", new[] { typeof(string), typeof(double), typeof(double) });
        Assert.NotNull(method);

        // Invoke with defaults for the doubles (reflection can't see the
        // C# default-parameter metadata as defaults the way a caller would,
        // so we pass them explicitly). A real consumer would do the same.
        var result = method!.Invoke(current, new object[] { "sys-zoran", 0.0, double.MaxValue });
        Assert.NotNull(result);

        // The result is IReadOnlyList<IReadOnlyDictionary<string, object?>> —
        // iterate without casting to any VGMissionLog type.
        var list = (IEnumerable)result!;
        var firstDict = FirstOrDefault<IReadOnlyDictionary<string, object?>>(list);
        Assert.NotNull(firstDict);

        Assert.Equal("evt-1",     firstDict!["eventId"]);
        Assert.Equal("Accepted",  firstDict["type"]);
        Assert.Equal("Bounty",    firstDict["missionType"]);
        Assert.Equal("sys-zoran", firstDict["sourceSystemId"]);
        Assert.Equal(42.0,        firstDict["gameSeconds"]);
    }

    [Fact]
    public void CountByType_ReturnsStringToInt_ViaReflection()
    {
        var current = GetCurrentViaReflection()!;
        var method  = current.GetType().GetMethod(
            "CountByType", new[] { typeof(double), typeof(double) });

        var result = method!.Invoke(current, new object[] { 0.0, double.MaxValue });
        var dict   = (IReadOnlyDictionary<string, int>)result!;

        Assert.Equal(1, dict["Bounty"]);
    }

    // --- helpers ----------------------------------------------------------

    private static object? GetCurrentViaReflection() =>
        Type.GetType("VGMissionLog.Api.MissionLogApi, VGMissionLog")!
            .GetProperty("Current")!
            .GetValue(null);

    private static T? FirstOrDefault<T>(IEnumerable source) where T : class
    {
        foreach (var item in source) return item as T;
        return null;
    }
}
