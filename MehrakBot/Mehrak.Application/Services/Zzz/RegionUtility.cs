#region

using Mehrak.Domain.Enums;

#endregion

namespace Mehrak.Application.Services.Zzz;

internal static class RegionUtility
{
    internal static string ToRegion(this Server server)
    {
        return server switch
        {
            Server.Asia => "prod_gf_jp",
            Server.Europe => "prod_gf_eu",
            Server.America => "prod_gf_us",
            Server.Sar => "prod_gf_sg",
            _ => throw new ArgumentException("Invalid server name")
        };
    }
}