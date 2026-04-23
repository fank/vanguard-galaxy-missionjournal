using BepInEx;

namespace VGMissionLog;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInProcess("VanguardGalaxy.exe")]
public class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "vgmissionlog";
    public const string PluginName = "Vanguard Galaxy Mission Log";
    public const string PluginVersion = "0.1.0";

    private void Awake()
    {
        Logger.LogInfo($"{PluginName} v{PluginVersion} loaded");
    }
}
