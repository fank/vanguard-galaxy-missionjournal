using System;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Source.Util;
using UnityEngine;
using VGMissionLog.Api;
using VGMissionLog.Config;
using VGMissionLog.Logging;
using VGMissionLog.Patches;
using VGMissionLog.Persistence;

namespace VGMissionLog;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInProcess("VanguardGalaxy.exe")]
public class Plugin : BaseUnityPlugin
{
    public const string PluginGuid    = "vgmissionlog";
    public const string PluginName    = "Vanguard Galaxy Mission Log";
    public const string PluginVersion = "0.1.0";

    internal static Plugin          Instance { get; private set; } = null!;
    internal static ManualLogSource Log      { get; private set; } = null!;

    internal ActivityLog          ActivityLog { get; private set; } = null!;
    internal IClock               Clock       { get; private set; } = null!;
    internal LogIO                Io          { get; private set; } = null!;
    internal ActivityEventBuilder Builder     { get; private set; } = null!;
    internal MissionLogConfig     Cfg         { get; private set; } = null!;

    private Harmony _harmony = null!;

    private void Awake()
    {
        Instance = this;
        Log      = Logger;

        // --- config (spec R4.5) -----------------------------------------
        Cfg = new MissionLogConfig(Config);

        // --- singletons -------------------------------------------------
        Clock       = new GameClock();
        Io          = new LogIO(() => DateTime.UtcNow);
        ActivityLog = new ActivityLog(
            maxEvents:        Cfg.MaxEvents.Value,
            onFirstEviction:  cap => Log.LogWarning(
                $"Activity log hit cap of {cap} events — oldest entries are now being evicted FIFO"));
        Builder     = new ActivityEventBuilder(Clock);

        if (Cfg.Verbose.Value)
        {
            ActivityLog.OnAppend += evt =>
                Log.LogDebug($"[vgml] {evt.Type} {evt.MissionType} storyId={evt.StoryId} @ {evt.GameSeconds:F1}s");
        }

        // --- wire every patch's static slots ----------------------------
        MissionAcceptPatch.Builder   = Builder;
        MissionAcceptPatch.Log       = ActivityLog;
        MissionAcceptPatch.BepLog    = Log;

        MissionCompletePatch.Builder = Builder;
        MissionCompletePatch.Log     = ActivityLog;
        MissionCompletePatch.BepLog  = Log;

        MissionFailPatch.Builder     = Builder;
        MissionFailPatch.Log         = ActivityLog;
        MissionFailPatch.BepLog      = Log;

        MissionAbandonPatch.Builder  = Builder;
        MissionAbandonPatch.Log      = ActivityLog;
        MissionAbandonPatch.BepLog   = Log;

        MissionArchivePatch.Builder  = Builder;
        MissionArchivePatch.Log      = ActivityLog;
        MissionArchivePatch.BepLog   = Log;

        SaveWritePatch.Log           = ActivityLog;
        SaveWritePatch.Io            = Io;
        SaveWritePatch.BepLog        = Log;

        SaveLoadPatch.Log            = ActivityLog;
        SaveLoadPatch.Io             = Io;
        SaveLoadPatch.BepLog         = Log;

        // --- patch attach -----------------------------------------------
        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll(typeof(MissionAcceptPatch));
        _harmony.PatchAll(typeof(MissionCompletePatch));
        _harmony.PatchAll(typeof(MissionFailPatch));
        _harmony.PatchAll(typeof(MissionAbandonPatch));
        _harmony.PatchAll(typeof(MissionArchivePatch));
        _harmony.PatchAll(typeof(SaveWritePatch));
        _harmony.PatchAll(typeof(SaveLoadPatch));

        // --- startup janitor (spec R3.2) --------------------------------
        try
        {
            var savesPath = SaveGame.SavesPath;
            var swept = DeadSidecarSweeper.Sweep(savesPath);
            if (swept.Count > 0)
                Log.LogInfo($"Swept {swept.Count} dead sidecar(s) from {savesPath}");
        }
        catch (Exception e)
        {
            Log.LogError($"Dead-sidecar sweep failed: {e}");
            // Non-fatal; journal continues running.
        }

        // --- safety-net quit-flush (spec R3.2) --------------------------
        Application.quitting += OnApplicationQuitting;

        // --- public facade (spec R4.2) ----------------------------------
        // Late-assigned last so consumers reflection-probing during our
        // startup never see a half-wired handle.
        MissionLogApi.Current = new MissionLogQueryAdapter(ActivityLog);

        var patchCount = _harmony.GetPatchedMethods().Count();
        Log.LogInfo($"{PluginName} v{PluginVersion} loaded ({patchCount} patched method(s))");
    }

    private void OnApplicationQuitting()
    {
        var path = SaveLoadPatch.LastKnownSavePath ?? SaveWritePatch.LastKnownSavePath;
        if (path is null) return;
        try
        {
            var sidecar = LogPathResolver.From(path);
            var schema  = new LogSchema(
                LogSchema.CurrentVersion,
                ActivityLog.AllEvents.ToArray());
            Io.Write(sidecar, schema);
            Log.LogInfo($"ApplicationQuit: flushed {schema.Events.Length} event(s) to {sidecar}");
        }
        catch (Exception e)
        {
            Log.LogError($"Quit-time flush failed: {e}");
        }
    }

    private void OnDestroy()
    {
        // Null the facade first so consumers reflection-probing after
        // teardown can't call into a torn-down adapter.
        MissionLogApi.Current = null;
        Application.quitting -= OnApplicationQuitting;
        _harmony?.UnpatchSelf();
    }
}
