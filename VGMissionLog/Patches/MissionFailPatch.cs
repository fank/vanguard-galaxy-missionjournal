using System;
using BepInEx.Logging;
using HarmonyLib;
using Source.MissionSystem;
using VGMissionLog.Logging;

namespace VGMissionLog.Patches;

/// <summary>
/// Postfix on <see cref="Mission.MissionFailed(string)"/> (decomp line
/// 36456). Appends a <see cref="TimelineState.Failed"/> transition to
/// the matching <see cref="MissionRecord"/>.
/// </summary>
[HarmonyPatch(typeof(Mission), nameof(Mission.MissionFailed))]
internal static class MissionFailPatch
{
    internal static MissionRecordBuilder Builder = null!;
    internal static MissionStore         Store   = null!;
    internal static ManualLogSource      BepLog  = null!;

    [HarmonyPostfix]
    private static void Postfix(Mission __instance, string reason)
    {
        if (__instance is null) return;
        try
        {
            var key      = Builder.GetInstanceId(__instance);
            var existing = Store.GetByInstanceId(key);
            if (existing is null) return;
            var next = Builder.AppendTransition(existing, TimelineState.Failed, __instance);
            Store.Upsert(next);
        }
        catch (Exception e)
        {
            BepLog.LogWarning($"MissionFailPatch swallowed (reason='{reason}'): {e}");
        }
    }
}
