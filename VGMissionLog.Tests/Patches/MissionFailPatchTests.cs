using System.Linq;
using System.Reflection;
using HarmonyLib;
using Source.MissionSystem;
using VGMissionLog.Patches;
using Xunit;

namespace VGMissionLog.Tests.Patches;

public class MissionFailPatchTests
{
    [Fact]
    public void TargetMethod_ResolvesTo_MissionFailed()
    {
        var target = typeof(Mission).GetMethod(
            nameof(Mission.MissionFailed),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { typeof(string) },
            modifiers: null);

        Assert.NotNull(target);
    }

    [Fact]
    public void HarmonyPatchAttribute_PointsAtMissionFailed()
    {
        var attr = typeof(MissionFailPatch)
            .GetCustomAttributes(typeof(HarmonyPatch), inherit: false)
            .Cast<HarmonyPatch>()
            .First();

        Assert.Equal(typeof(Mission),                attr.info.declaringType);
        Assert.Equal(nameof(Mission.MissionFailed),  attr.info.methodName);
    }

    [Fact]
    public void Postfix_IsStaticPrivate_WithHarmonyPostfix()
    {
        var postfix = typeof(MissionFailPatch).GetMethod(
            "Postfix", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(postfix);
        Assert.NotNull(postfix!.GetCustomAttribute<HarmonyPostfix>());
    }
}
