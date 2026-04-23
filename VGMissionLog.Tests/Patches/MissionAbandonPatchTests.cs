using System.Linq;
using System.Reflection;
using HarmonyLib;
using Source.MissionSystem;
using Source.Player;
using VGMissionLog.Patches;
using Xunit;

namespace VGMissionLog.Tests.Patches;

public class MissionAbandonPatchTests
{
    [Fact]
    public void TargetMethod_ResolvesTo_RemoveMission_MissionBoolOverload()
    {
        var target = typeof(GamePlayer).GetMethod(
            nameof(GamePlayer.RemoveMission),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { typeof(Mission), typeof(bool) },
            modifiers: null);

        Assert.NotNull(target);
    }

    [Fact]
    public void HarmonyPatchAttribute_PointsAtRemoveMission()
    {
        var attr = typeof(MissionAbandonPatch)
            .GetCustomAttributes(typeof(HarmonyPatch), inherit: false)
            .Cast<HarmonyPatch>()
            .First();

        Assert.Equal(typeof(GamePlayer),               attr.info.declaringType);
        Assert.Equal(nameof(GamePlayer.RemoveMission), attr.info.methodName);
    }

    [Fact]
    public void Postfix_IsStaticPrivate_WithHarmonyPostfix()
    {
        var postfix = typeof(MissionAbandonPatch).GetMethod(
            "Postfix", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(postfix);
        Assert.NotNull(postfix!.GetCustomAttribute<HarmonyPostfix>());
    }
}
