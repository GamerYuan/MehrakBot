#region

#endregion

using Mehrak.Domain.Enums;

namespace Mehrak.Application.Services.Genshin;

internal static class RegionUtility
{
    internal static string ToRegion(this Server server)
    {
        return server switch
        {
            Server.Asia => "os_asia",
            Server.Europe => "os_euro",
            Server.America => "os_usa",
            Server.Sar => "os_cht",
            _ => throw new ArgumentException("Invalid server name")
        };
    }
}
