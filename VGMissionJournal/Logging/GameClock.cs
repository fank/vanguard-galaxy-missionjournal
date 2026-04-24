using System;
using Source.Player;

namespace VGMissionJournal.Logging;

/// <summary>
/// Production <see cref="IClock"/>. Game seconds come from
/// <see cref="GamePlayer.current"/>'s <c>elapsedTime</c> accumulator —
/// vanilla's own game-clock concept, updated each frame by
/// <c>Time.deltaTime</c> (see decomp line 33448) and serialized to the
/// save (line 34364). Falls back to 0 when there is no active player
/// (boot, pre-load, tests). Real time is <see cref="DateTime.UtcNow"/>.
/// </summary>
internal sealed class GameClock : IClock
{
    public double GameSeconds => GamePlayer.current?.elapsedTime ?? 0.0;

    public DateTime UtcNow => DateTime.UtcNow;
}
