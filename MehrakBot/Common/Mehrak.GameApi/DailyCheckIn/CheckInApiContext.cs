#region

using Mehrak.Domain.Shared.Abstractions;
using Mehrak.Domain.Shared.Enums;

#endregion

namespace Mehrak.GameApi.DailyCheckIn;

public class CheckInApiContext : IApiContext
{
    public ulong UserId { get; }
    public ulong LtUid { get; }
    public string LToken { get; }
    public Game Game { get; }

    public CheckInApiContext(ulong userId, ulong ltuid, string ltoken, Game game)
    {
        UserId = userId;
        LtUid = ltuid;
        LToken = ltoken;
        Game = game;
    }
}