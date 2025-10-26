#region

using Mehrak.Domain.Models.Abstractions;

#endregion

namespace Mehrak.GameApi.Common.Types;

public class BaseHoYoApiContext : IApiContext
{
    public ulong UserId { get; }

    public ulong LtUid { get; }
    public string LToken { get; }
    public string? GameUid { get; }
    public string? Region { get; }

    public BaseHoYoApiContext(ulong userId, ulong ltuid,
        string lToken, string? gameUid, string? region)
    {
        UserId = userId;
        LtUid = ltuid;
        LToken = lToken;
        GameUid = gameUid;
        Region = region;
    }
}