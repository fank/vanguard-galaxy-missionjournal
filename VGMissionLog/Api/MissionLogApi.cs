namespace VGMissionLog.Api;

/// <summary>
/// Static facade that cross-mod consumers reach via reflection:
/// <code>
/// var facadeType = Type.GetType("VGMissionLog.Api.MissionLogApi, VGMissionLog");
/// var current    = facadeType?.GetProperty("Current")?.GetValue(null);
/// </code>
/// <para><see cref="Current"/> is <c>null</c> until VGMissionLog's
/// <c>Plugin.Awake</c> has run — consumers must null-check and gracefully
/// degrade when the plugin isn't installed. Per spec R5.5, VGMissionLog
/// is always the <i>producer</i> (eager) and consumers are late-binding.</para>
/// </summary>
public static class MissionLogApi
{
    /// <summary>Live query handle, or <c>null</c> when the plugin isn't
    /// loaded. Set by <c>Plugin.Awake</c> in ML-T5b; cleared in
    /// <c>Plugin.OnDestroy</c>.</summary>
    public static IMissionLogQuery? Current { get; internal set; }
}
