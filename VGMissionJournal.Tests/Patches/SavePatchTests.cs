using System.Linq;
using System.Reflection;
using HarmonyLib;
using Source.Util;
using VGMissionJournal.Patches;
using Xunit;

namespace VGMissionJournal.Tests.Patches;

public class SavePatchTests
{
    // --- SaveWritePatch ---------------------------------------------------

    [Fact]
    public void SaveWrite_TargetMethod_ResolvesTo_SaveGame_Store()
    {
        var target = typeof(SaveGame).GetMethod(nameof(SaveGame.Store),
            BindingFlags.Static | BindingFlags.Public);
        Assert.NotNull(target);
    }

    [Fact]
    public void SaveWrite_Attribute_PointsAtSaveGameStore()
    {
        var attr = typeof(SaveWritePatch)
            .GetCustomAttributes(typeof(HarmonyPatch), inherit: false)
            .Cast<HarmonyPatch>()
            .First();
        Assert.Equal(typeof(SaveGame),       attr.info.declaringType);
        Assert.Equal(nameof(SaveGame.Store), attr.info.methodName);
    }

    [Fact]
    public void SaveWrite_Postfix_IsStaticPrivate_WithHarmonyPostfix()
    {
        var m = typeof(SaveWritePatch).GetMethod("Postfix",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(m);
        Assert.NotNull(m!.GetCustomAttribute<HarmonyPostfix>());
    }

    // --- SaveLoadPatch ---------------------------------------------------

    [Fact]
    public void SaveLoad_TargetMethod_ResolvesTo_SaveGameFile_LoadSaveGame()
    {
        var target = typeof(SaveGameFile).GetMethod(nameof(SaveGameFile.LoadSaveGame),
            BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(target);
    }

    [Fact]
    public void SaveLoad_Attribute_PointsAtLoadSaveGame()
    {
        var attr = typeof(SaveLoadPatch)
            .GetCustomAttributes(typeof(HarmonyPatch), inherit: false)
            .Cast<HarmonyPatch>()
            .First();
        Assert.Equal(typeof(SaveGameFile),                   attr.info.declaringType);
        Assert.Equal(nameof(SaveGameFile.LoadSaveGame),       attr.info.methodName);
    }

    [Fact]
    public void SaveLoad_Prefix_IsStaticPrivate_WithHarmonyPrefix()
    {
        var m = typeof(SaveLoadPatch).GetMethod("Prefix",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(m);
        Assert.NotNull(m!.GetCustomAttribute<HarmonyPrefix>());
    }
}
