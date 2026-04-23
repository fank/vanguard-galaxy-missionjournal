using System.Collections.Generic;

namespace VGMissionLog.Logging;

/// <summary>
/// Snapshot of a single <c>MissionObjective</c>. We record the raw
/// subclass name (<c>"KillEnemies"</c>, <c>"TravelToPOI"</c>, <c>"Mining"</c>,
/// <c>"TradeOffer"</c>, <c>"CollectCredits"</c>, <c>"Reputation"</c>,
/// <c>"Crafting"</c>, …) and whatever primitive-ish public state the
/// concrete objective exposes. Consumers bucket and interpret.
///
/// <para><b>Fields:</b>
/// <list type="bullet">
///   <item><c>Type</c> — <c>objective.GetType().Name</c>. The raw signal
///         for what this objective asks of the player.</item>
///   <item><c>IsComplete</c> — <c>objective.IsComplete()</c>. Vanilla's
///         own method; read-only in every concrete subclass (simple
///         counter comparisons or world-state reads).</item>
///   <item><c>StatusText</c> — the game's translated progress string
///         (e.g. "Kill 3/5 Pirates"). Best-effort; may be null if the
///         localizer isn't ready or the objective throws on read.</item>
///   <item><c>Fields</c> — typed best-effort read of public primitive
///         fields/properties on the objective (int/long/bool/float/
///         double/string/enum), plus resolved identifiers for Faction
///         and InventoryItemType references. Keys are camelCase. Null
///         when extraction fails.</item>
/// </list></para>
/// </summary>
public sealed record MissionObjectiveSnapshot(
    string Type,
    bool IsComplete,
    string? StatusText,
    IReadOnlyDictionary<string, object?>? Fields);
