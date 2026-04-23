using System;
using BepInEx.Logging;
using HarmonyLib;
using Source.MissionSystem;
using VGMissionLog.Logging;

namespace VGMissionLog.Patches;

/// <summary>
/// Postfix on <see cref="Mission.MissionFailed(string)"/> (decomp line
/// 36456). Emits a <see cref="ActivityEventType.Failed"/> event when a
/// mission's fail condition fires.
///
/// <para>Vanilla's <c>MissionFailed</c> sets <c>failed = true</c> and
/// shows a red notification; it does NOT remove the mission from the
/// player's list. The mission typically gets cleaned up later via an
/// explicit player abandon (→ our <see cref="MissionAbandonPatch"/>) —
/// so a fail-then-abandon sequence legitimately emits two events. That's
/// the correct semantic per spec R1.1 (Failed = terminal condition hit,
/// Abandoned = player dropped).</para>
///
/// <para>Patch target is an instance method, so Harmony passes the
/// failing mission via <c>__instance</c>. The <c>reason</c> arg is
/// captured but not currently propagated into the event (spec R1.2 has
/// no reason field; reserved for a future additive schema bump).</para>
/// </summary>
[HarmonyPatch(typeof(Mission), nameof(Mission.MissionFailed))]
internal static class MissionFailPatch
{
    internal static ActivityEventBuilder Builder = null!;
    internal static ActivityLog          Log     = null!;
    internal static ManualLogSource      BepLog  = null!;

    [HarmonyPostfix]
    private static void Postfix(Mission __instance, string reason)
    {
        if (__instance is null) return;
        try
        {
            var evt = Builder.Build(__instance, ActivityEventType.Failed);
            Log.Append(evt);
        }
        catch (Exception e)
        {
            BepLog.LogWarning($"MissionFailPatch swallowed (reason='{reason}'): {e}");
        }
    }
}
