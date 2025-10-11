using Mehrak.Domain.Enums;
using Mehrak.Domain.Models.Abstractions;

namespace Mehrak.GameApi.Common.Types;

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
