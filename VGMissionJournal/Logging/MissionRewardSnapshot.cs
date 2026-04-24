using System.Collections.Generic;

namespace VGMissionJournal.Logging;

/// <summary>
/// Snapshot of one <c>MissionReward</c> entry. Vanilla ships 14 concrete
/// reward subtypes (Credits, Experience, Reputation, Item, Ship, Crew,
/// Skilltree, Skillpoint, StoryMission, MissionFollowUp, POICoordinates,
/// UmbralControl, WorkshopCredit, ConquestStrength). The typed top-level
/// <c>RewardsCredits</c> / <c>RewardsExperience</c> / <c>RewardsReputation</c>
/// keys on <see cref="ActivityEvent"/> cover only the numeric money/XP/rep
/// subset; this snapshot exposes everything else by capturing whatever
/// primitive-like public state each subclass exposes.
///
/// <para><b>Fields:</b>
/// <list type="bullet">
///   <item><c>Type</c> — <c>reward.GetType().Name</c>. Consumers match
///         on this to decide how to render the reward (e.g. <c>"Item"</c>
///         vs <c>"Ship"</c> vs <c>"Skilltree"</c>).</item>
///   <item><c>Fields</c> — typed best-effort read of public primitive
///         fields/properties on the reward. Faction / InventoryItemType /
///         MapElement references resolve to their stable identifiers.
///         Null when extraction fails or yields nothing.</item>
/// </list></para>
/// </summary>
public sealed record MissionRewardSnapshot(
    string Type,
    IReadOnlyDictionary<string, object?>? Fields);
