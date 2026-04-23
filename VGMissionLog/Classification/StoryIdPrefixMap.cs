using System;

namespace VGMissionLog.Classification;

/// <summary>
/// Extracts a namespace-like prefix from a vanilla storyId so missions
/// authored by third-party mods can be grouped under
/// <see cref="Logging.MissionType.ThirdParty"/>.
///
/// Default heuristic: everything up to a <c>_llm_</c> infix. This matches
/// VGAnima's own convention (<c>vganima_llm_abc123</c> → <c>vganima</c>)
/// out of the box and is deliberately narrow so vanilla storyIds that
/// happen to contain underscores (<c>tutorial_1</c>, <c>sidequest_x</c>)
/// don't get mis-tagged as third-party.
///
/// ML-T2b adds a configurable registration API for prefixes that don't
/// follow the <c>_llm_</c> convention.
/// </summary>
internal static class StoryIdPrefixMap
{
    internal const string DefaultInfix = "_llm_";

    /// <summary>
    /// Returns the prefix segment (non-empty, pre-infix) when the storyId
    /// follows a recognised third-party convention, else <c>null</c>.
    /// </summary>
    public static string? ExtractPrefix(string? storyId)
    {
        if (string.IsNullOrEmpty(storyId)) return null;

        var idx = storyId!.IndexOf(DefaultInfix, StringComparison.Ordinal);
        if (idx <= 0) return null;          // needs at least one char before the infix
        return storyId.Substring(0, idx);
    }
}
