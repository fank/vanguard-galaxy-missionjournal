namespace VGMissionLog.Logging;

/// <summary>
/// A single faction-reputation reward applied at mission completion.
/// Spec R1.2 (<c>rewardsReputation</c>).
/// </summary>
public sealed record RepReward(string Faction, int Amount);
