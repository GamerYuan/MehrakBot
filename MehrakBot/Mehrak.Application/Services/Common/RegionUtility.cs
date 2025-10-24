#region

#endregion

using Mehrak.Domain.Enums;

namespace Mehrak.Application.Services.Common;

internal static class RegionUtility
{
    internal static string ToRegion(this Server server, Game game)
    {
        return game switch
        {
            Game.Genshin => Genshin.RegionUtility.ToRegion(server),
            Game.HonkaiStarRail => Hsr.RegionUtility.ToRegion(server),
            Game.ZenlessZoneZero => Zzz.RegionUtility.ToRegion(server),
            _ => throw new ArgumentException("Invalid game for region conversion")
        };
    }
}
