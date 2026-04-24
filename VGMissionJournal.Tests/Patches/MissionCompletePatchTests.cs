using System.Linq;
using System.Reflection;
using HarmonyLib;
using Source.MissionSystem;
using Source.Player;
using VGMissionJournal.Patches;
using Xunit;

namespace VGMissionJournal.Tests.Patches;

public class MissionCompletePatchTests
{
    [Fact]
    public void TargetMethod_ResolvesTo_CompleteMission_MissionBoolOverload()
    {
        var target = typeof(GamePlayer).GetMethod(
            nameof(GamePlayer.CompleteMission),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { typeof(Mission), typeof(bool) },
            modifiers: null);

        Assert.NotNull(target);
    }

    [Fact]
    public void HarmonyPatchAttribute_PointsAtExpectedTarget()
    {
        var attr = typeof(MissionCompletePatch)
            .GetCustomAttributes(typeof(HarmonyPatch), inherit: false)
            .Cast<HarmonyPatch>()
            .First();

        Assert.Equal(typeof(GamePlayer),                attr.info.declaringType);
        Assert.Equal(nameof(GamePlayer.CompleteMission), attr.info.methodName);
        Assert.Contains(typeof(Mission), attr.info.argumentTypes);
        Assert.Contains(typeof(bool),    attr.info.argumentTypes);
    }

    [Fact]
    public void Postfix_IsStaticPrivate_WithHarmonyPostfix()
    {
        var postfix = typeof(MissionCompletePatch).GetMethod(
            "Postfix", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(postfix);
        Assert.NotNull(postfix!.GetCustomAttribute<HarmonyPostfix>());
    }
}
