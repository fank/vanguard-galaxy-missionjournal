using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Source.Galaxy;
using Source.Player;
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

    internal MissionStore         Store   { get; private set; } = null!;
    internal IClock               Clock   { get; private set; } = null!;
    internal LogIO                Io      { get; private set; } = null!;
    internal MissionRecordBuilder Builder { get; private set; } = null!;
    internal MissionLogConfig     Cfg     { get; private set; } = null!;

    private Harmony _harmony = null!;

    // Reflection-resolved once — MapElement.<guid>k__BackingField on the
    // player's current POI -> system.
    private static readonly FieldInfo? _mapElementGuidField =
        typeof(Source.Galaxy.MapElement).GetField(
            "<guid>k__BackingField",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static string? ResolvePlayerCurrentSystemId()
    {
        try
        {
            var player = GamePlayer.current;
            var system = player?.currentPointOfInterest?.system;
            if (system is null || _mapElementGuidField is null) return null;
            return _mapElementGuidField.GetValue(system) as string;
        }
        catch
        {
            return null;
        }
    }

    private void Awake()
    {
        Instance = this;
        Log      = Logger;

        // --- config (spec R4.5) -----------------------------------------
        Cfg = new MissionLogConfig(Config);

        // --- singletons -------------------------------------------------
        Clock   = new GameClock();
        Io      = new LogIO(() => DateTime.UtcNow);
        Store   = new MissionStore(
            maxMissions:     Cfg.MaxEvents.Value,
            onFirstEviction: cap => Log.LogWarning(
                $"Mission store hit cap of {cap} missions — oldest entries are now being evicted FIFO"));
        Builder = new MissionRecordBuilder(Clock, ResolvePlayerCurrentSystemId);

        if (Cfg.Verbose.Value)
        {
            Store.OnMissionChanged += r =>
            {
                var last = r.Timeline[r.Timeline.Count - 1];
                Log.LogDebug($"{last.State} {r.MissionSubclass} instanceId={r.MissionInstanceId} @ {last.GameSeconds:F1}s");
            };
        }

        // --- wire every patch's static slots ----------------------------
        PatchWiring.WireAll(Builder, Store, Io, Log);

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
        }

        // --- safety-net quit-flush (spec R3.2) --------------------------
        Application.quitting += OnApplicationQuitting;

        // --- public facade (spec R4.2) ----------------------------------
        MissionLogApi.Current = new MissionLogQueryAdapter(Store);

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
                Store.AllMissions.ToArray());
            Io.Write(sidecar, schema);
            Log.LogInfo($"ApplicationQuit: flushed {schema.Missions.Length} mission(s) to {sidecar}");
        }
        catch (Exception e)
        {
            Log.LogError($"Quit-time flush failed: {e}");
        }
    }

    private void OnDestroy()
    {
        MissionLogApi.Current = null;
        Application.quitting -= OnApplicationQuitting;
        _harmony?.UnpatchSelf();
    }
}
