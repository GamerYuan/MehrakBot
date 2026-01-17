using Mehrak.Domain.Enums;

namespace Mehrak.Application.Services.Hi3;

internal static class RegionUtility
{
    public static string ToRegion(this Hi3Server server)
    {
        return server switch
        {
            Hi3Server.SEA => "overseas01",
            Hi3Server.JP => "jp01",
            Hi3Server.KR => "kr01",
            Hi3Server.America => "usa01",
            Hi3Server.SAR => "asia01",
            Hi3Server.Europe => "eur01",
            _ => throw new ArgumentOutOfRangeException(nameof(server), server, null)
        };
    }
}
