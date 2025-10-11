using Mehrak.Domain.Services.Abstractions;

namespace Mehrak.GameApi.Common.Types;

public class BaseHoYoApiContext : IApiContext
{
    public ulong UserId { get; private init; }

    public ulong LtUid { get; private init; }
    public string LToken { get; private init; }
    public string? GameUid { get; private init; }
    public string? Region { get; private init; }

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
