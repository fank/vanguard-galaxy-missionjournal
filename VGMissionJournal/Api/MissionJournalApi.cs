namespace VGMissionJournal.Api;

/// <summary>
/// Static facade that cross-mod consumers reach via reflection:
/// <code>
/// var facadeType = Type.GetType("VGMissionJournal.Api.MissionJournalApi, VGMissionJournal");
/// var current    = facadeType?.GetProperty("Current")?.GetValue(null);
/// </code>
/// <para><see cref="Current"/> is <c>null</c> until VGMissionJournal's
/// <c>Plugin.Awake</c> has run — consumers must null-check and gracefully
/// degrade when the plugin isn't installed. Per spec R5.5, VGMissionJournal
/// is always the <i>producer</i> (eager) and consumers are late-binding.</para>
/// </summary>
public static class MissionJournalApi
{
    /// <summary>Live query handle, or <c>null</c> when the plugin isn't
    /// loaded. Set by <c>Plugin.Awake</c> in ML-T5b; cleared in
    /// <c>Plugin.OnDestroy</c>.</summary>
    public static IMissionJournalQuery? Current { get; internal set; }
}
