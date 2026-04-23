using System;
using BepInEx.Logging;
using HarmonyLib;
using Source.MissionSystem;
using Source.Player;
using VGMissionLog.Logging;

namespace VGMissionLog.Patches;

/// <summary>
/// Postfix on <see cref="GamePlayer.AddMissionWithLog(Mission)"/>
/// (decomp line 34880). Emits an <see cref="ActivityEventType.Accepted"/>
/// event whenever vanilla registers a mission as "in progress" for the
/// player.
///
/// <para>Vanilla also has a string-overload of
/// <c>AddMissionWithLog(string)</c> at line 34911 that resolves a
/// <c>StoryMission</c> template and dispatches through the
/// <c>Mission</c>-overload we patch here — so this single hook covers
/// both acceptance paths.</para>
///
/// <para>Exception safety per spec R5.2: the postfix body is wrapped in
/// try/catch; any failure warn-logs and is swallowed. Vanilla execution
/// must survive our internal state.</para>
/// </summary>
[HarmonyPatch(typeof(GamePlayer), nameof(GamePlayer.AddMissionWithLog), new[] { typeof(Mission) })]
internal static class MissionAcceptPatch
{
    // Wired by Plugin.Awake in ML-T4i. The patch is static (Harmony
    // requires it) so we ambient-inject singletons rather than
    // constructor-inject.
    internal static ActivityEventBuilder Builder = null!;
    internal static ActivityLog          Log     = null!;
    internal static ManualLogSource      BepLog  = null!;

    [HarmonyPostfix]
    private static void Postfix(Mission mission)
    {
        if (mission is null) return;
        try
        {
            var evt = Builder.Build(mission, ActivityEventType.Accepted);
            Log.Append(evt);
        }
        catch (Exception e)
        {
            BepLog.LogWarning($"MissionAcceptPatch swallowed: {e}");
        }
    }
}
