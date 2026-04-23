using System;
using System.Collections.Generic;

namespace VGMissionLog.Classification;

/// <summary>
/// Extracts a namespace-like prefix from a vanilla storyId so missions
/// authored by third-party mods can be grouped under
/// <see cref="Logging.MissionType.ThirdParty"/>.
///
/// <para>Default convention: everything up to a <c>_llm_</c> infix. This
/// matches VGAnima's own convention (<c>vganima_llm_abc123</c> →
/// <c>vganima</c>) out of the box and is deliberately narrow so vanilla
/// storyIds that happen to contain underscores (<c>tutorial_1</c>,
/// <c>sidequest_x</c>) don't get mis-tagged as third-party.</para>
///
/// <para>Third-party conventions that don't use <c>_llm_</c> can register
/// their own infix via <see cref="Register"/>. Registration is additive
/// (the built-in <c>_llm_</c> always remains recognised) and first-match-
/// wins in registration order, so registered infixes take precedence over
/// the default only when matched earlier in the storyId.</para>
/// </summary>
internal static class StoryIdPrefixMap
{
    internal const string DefaultInfix = "_llm_";

    private static readonly object _lock = new();
    private static readonly List<string> _infixes = new() { DefaultInfix };

    /// <summary>
    /// Add an infix substring that delimits a third-party prefix in a
    /// storyId. Calls are idempotent and thread-safe. Empty / null values
    /// are rejected.
    /// </summary>
    public static void Register(string infix)
    {
        if (string.IsNullOrEmpty(infix))
            throw new ArgumentException("Infix must be non-empty.", nameof(infix));

        lock (_lock)
        {
            if (!_infixes.Contains(infix)) _infixes.Add(infix);
        }
    }

    /// <summary>
    /// Returns the prefix segment (non-empty, pre-infix) when the storyId
    /// matches any registered infix, else <c>null</c>. First-match-wins in
    /// registration order; the built-in <c>_llm_</c> is checked first.
    /// </summary>
    public static string? ExtractPrefix(string? storyId)
    {
        if (string.IsNullOrEmpty(storyId)) return null;

        // Snapshot under lock; extraction itself is pure on the snapshot so
        // the lock isn't held across caller workloads.
        string[] infixes;
        lock (_lock) { infixes = _infixes.ToArray(); }

        foreach (var infix in infixes)
        {
            var idx = storyId!.IndexOf(infix, StringComparison.Ordinal);
            if (idx > 0) return storyId.Substring(0, idx);
        }
        return null;
    }

    /// <summary>Test-only: restore the registered set to the built-in default.</summary>
    internal static void ResetToDefault()
    {
        lock (_lock)
        {
            _infixes.Clear();
            _infixes.Add(DefaultInfix);
        }
    }
}
