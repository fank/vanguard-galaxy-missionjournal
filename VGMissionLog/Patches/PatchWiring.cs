using BepInEx.Logging;
using VGMissionLog.Logging;
using VGMissionLog.Persistence;

namespace VGMissionLog.Patches;

/// <summary>
/// Centralises the static-slot wiring for every patch class. Keeping this in
/// one place makes it impossible to forget a slot when adding a new patch —
/// <see cref="AssertAllWired"/> is exercised by a test that reflection-scans
/// every <c>*Patch</c> type and verifies each slot is non-null after
/// <see cref="WireAll"/> runs.
/// </summary>
internal static class PatchWiring
{
    public static void WireAll(
        ActivityEventBuilder builder,
        ActivityLog          log,
        LogIO                io,
        ManualLogSource      bepLog)
    {
        MissionAcceptPatch.Builder   = builder;
        MissionAcceptPatch.Log       = log;
        MissionAcceptPatch.BepLog    = bepLog;

        MissionCompletePatch.Builder = builder;
        MissionCompletePatch.Log     = log;
        MissionCompletePatch.BepLog  = bepLog;

        MissionFailPatch.Builder     = builder;
        MissionFailPatch.Log         = log;
        MissionFailPatch.BepLog      = bepLog;

        MissionAbandonPatch.Builder  = builder;
        MissionAbandonPatch.Log      = log;
        MissionAbandonPatch.BepLog   = bepLog;

        MissionArchivePatch.Builder  = builder;
        MissionArchivePatch.Log      = log;
        MissionArchivePatch.BepLog   = bepLog;

        SaveWritePatch.Log           = log;
        SaveWritePatch.Io            = io;
        SaveWritePatch.BepLog        = bepLog;

        SaveLoadPatch.Log            = log;
        SaveLoadPatch.Io             = io;
        SaveLoadPatch.BepLog         = bepLog;
    }
}
