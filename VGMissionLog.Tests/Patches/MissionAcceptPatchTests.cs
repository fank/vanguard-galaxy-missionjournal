using System.Linq;
using System.Reflection;
using HarmonyLib;
using Source.MissionSystem;
using Source.Player;
using VGMissionLog.Patches;
using Xunit;

namespace VGMissionLog.Tests.Patches;

public class MissionAcceptPatchTests
{
    [Fact]
    public void TargetMethod_ResolvesTo_AddMissionWithLog_MissionOverload()
    {
        // Smoke test for rename drift (spec R6.2). If vanilla renames
        // `AddMissionWithLog` or changes its signature, this fails fast
        // rather than silently no-op'ing at Harmony.PatchAll time.
        var target = typeof(GamePlayer).GetMethod(
            nameof(GamePlayer.AddMissionWithLog),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { typeof(Mission) },
            modifiers: null);

        Assert.NotNull(target);
    }

    [Fact]
    public void HarmonyPatchAttribute_PointsAtExpectedTarget()
    {
        var attr = typeof(MissionAcceptPatch)
            .GetCustomAttributes(typeof(HarmonyPatch), inherit: false)
            .Cast<HarmonyPatch>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal(typeof(GamePlayer),                attr!.info.declaringType);
        Assert.Equal(nameof(GamePlayer.AddMissionWithLog), attr.info.methodName);
        Assert.Contains(typeof(Mission),                attr.info.argumentTypes);
    }

    [Fact]
    public void Postfix_IsStaticPrivate_MethodNamedPostfix()
    {
        var postfix = typeof(MissionAcceptPatch).GetMethod(
            "Postfix", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(postfix);
        Assert.NotNull(postfix!.GetCustomAttribute<HarmonyPostfix>());
    }
}
