using System.Collections.Frozen;

namespace Mehrak.Domain.Shared.Enums;

// ponytail: update these when game level caps change
public static class GameMaxLevels
{
    private static readonly FrozenDictionary<Game, int> MaxLevels = new Dictionary<Game, int>
    {
        [Game.Genshin] = 60,
        [Game.HonkaiStarRail] = 70,
        [Game.ZenlessZoneZero] = 60,
        [Game.HonkaiImpact3] = 88
    }.ToFrozenDictionary();

    public static int GetMaxLevel(this Game game) => MaxLevels.GetValueOrDefault(game, 0);
}
