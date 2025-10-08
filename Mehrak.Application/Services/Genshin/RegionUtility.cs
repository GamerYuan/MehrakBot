#region

using MehrakCore.Utility;

#endregion

namespace Mehrak.Application.Services.Genshin;

internal static class RegionUtility
{
    internal static string GetRegion(this Regions server)
    {
        return server switch
        {
            Regions.Asia => "os_asia",
            Regions.Europe => "os_euro",
            Regions.America => "os_usa",
            Regions.Sar => "os_cht",
            _ => throw new ArgumentException("Invalid server name")
        };
    }
}