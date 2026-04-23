using System;
using System.IO;
using BepInEx.Logging;
using HarmonyLib;
using Source.Util;
using VGMissionLog.Logging;
using VGMissionLog.Persistence;

namespace VGMissionLog.Patches;

/// <summary>
/// Postfix on <see cref="SaveGame.Store"/> (decomp line 22163) —
/// flushes the in-memory <see cref="ActivityLog"/> to its sidecar after
/// vanilla's own save succeeds.
///
/// <para>Target is a static method so Harmony doesn't pass
/// <c>__instance</c>; we reconstruct the vanilla save path from
/// <paramref name="saveName"/> (the 2nd arg) plus
/// <see cref="SaveGame.SavesPath"/>. This mirrors vanilla's own path
/// construction at decomp line 22166:
/// <c>SavesDir.FullName + "/" + saveName + ".save"</c>.</para>
///
/// <para>Scout note (ML-T4a): on failure, vanilla recursively self-
/// invokes Store with <c>attempt+1</c> up to 5 times. Each call fires
/// our postfix, so we'd re-flush the same sidecar up to 5 times —
/// harmless because the write is idempotent and we're flushing the
/// same in-memory snapshot. Not worth gating on <c>attempt</c>.</para>
///
/// <para><see cref="LastKnownSavePath"/> is tracked for the
/// <c>Application.quitting</c> safety-net flush in Plugin.Awake
/// (spec R3.2).</para>
/// </summary>
[HarmonyPatch(typeof(SaveGame), nameof(SaveGame.Store))]
internal static class SaveWritePatch
{
    internal static ActivityLog      Log     = null!;
    internal static LogIO            Io      = null!;
    internal static ManualLogSource  BepLog  = null!;

    internal static string? LastKnownSavePath { get; private set; }

    [HarmonyPostfix]
    private static void Postfix(string saveName)
    {
        if (string.IsNullOrEmpty(saveName)) return;
        try
        {
            var savePath = Path.Combine(SaveGame.SavesPath, saveName + ".save");
            var sidecar  = LogPathResolver.From(savePath);
            var schema   = new LogSchema(
                LogSchema.CurrentVersion,
                System.Linq.Enumerable.ToArray(Log.AllEvents));
            Io.Write(sidecar, schema);
            LastKnownSavePath = savePath;
            BepLog.LogInfo($"Flushed {schema.Events.Length} event(s) to {sidecar}");
        }
        catch (Exception e)
        {
            BepLog.LogWarning($"SaveWritePatch swallowed (saveName='{saveName}'): {e}");
        }
    }
}
