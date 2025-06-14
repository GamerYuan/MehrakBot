#region

using MehrakCore.Utility;

#endregion

namespace MehrakCore.Services.Commands.Hsr;

internal static class RegionUtility
{
    internal static string GetRegion(this Regions server)
    {
        return server switch
        {
            Regions.Asia => "prod_official_asia",
            Regions.Europe => "prod_official_eur",
            Regions.America => "prod_official_usa",
            Regions.Sar => "prod_official_cht",
            _ => throw new ArgumentException("Invalid server name")
        };
    }
}