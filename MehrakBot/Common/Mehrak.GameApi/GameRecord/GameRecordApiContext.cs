#region

using Mehrak.Domain.Shared.Abstractions;

#endregion

namespace Mehrak.GameApi.GameRecord;

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