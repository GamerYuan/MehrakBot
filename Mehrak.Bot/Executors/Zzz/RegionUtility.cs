#region

using MehrakCore.Utility;

#endregion

namespace Mehrak.Bot.Executors.Zzz;

internal static class RegionUtility
{
    internal static string GetRegion(this Regions server)
    {
        return server switch
        {
            Regions.Asia => "prod_gf_jp",
            Regions.Europe => "prod_gf_eu",
            Regions.America => "prod_gf_us",
            Regions.Sar => "prod_gf_sg",
            _ => throw new ArgumentException("Invalid server name")
        };
    }
}
