using Mehrak.Domain.Models.Abstractions;

namespace Mehrak.GameApi.Common.Types;

public class GameRecordApiContext : IApiContext
{
    public ulong UserId { get; }
    public ulong LtUid { get; }
    public string LToken { get; }

    public GameRecordApiContext(ulong userId, ulong ltuid, string ltoken)
    {
        UserId = userId;
        LtUid = ltuid;
        LToken = ltoken;
    }
}
