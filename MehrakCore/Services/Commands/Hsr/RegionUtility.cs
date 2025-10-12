#region

using MehrakCore.Utility;

#endregion

namespace MehrakCore.Services.Commands.Hsr;

internal static class RegionUtility
{
    internal static string GetRegion(this Server server)
    {
        return server switch
        {
            Server.Asia => "prod_official_asia",
            Server.Europe => "prod_official_eur",
            Server.America => "prod_official_usa",
            Server.Sar => "prod_official_cht",
            _ => throw new ArgumentException("Invalid server name")
        };
    }
}