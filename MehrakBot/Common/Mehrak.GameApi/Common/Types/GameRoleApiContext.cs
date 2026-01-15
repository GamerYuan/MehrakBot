#region

using Mehrak.Domain.Enums;
using Mehrak.Domain.Models.Abstractions;

#endregion

namespace Mehrak.GameApi.Common.Types;

public class GameRoleApiContext : IApiContext
{
    public ulong UserId { get; }
    public ulong LtUid { get; }
    public string LToken { get; }
    public Game Game { get; }
    public string Region { get; }

    public GameRoleApiContext(ulong userId, ulong ltuid, string ltoken, Game game, string region)
    {
        UserId = userId;
        LtUid = ltuid;
        LToken = ltoken;
        Game = game;
        Region = region;
    }
}