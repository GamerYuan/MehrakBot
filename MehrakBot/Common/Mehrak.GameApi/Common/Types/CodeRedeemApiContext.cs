#region

using Mehrak.Domain.Enums;

#endregion

namespace Mehrak.GameApi.Common.Types;

public class CodeRedeemApiContext : BaseHoYoApiContext
{
    public Game Game { get; }
    public string Code { get; }

    public CodeRedeemApiContext(ulong userId, ulong ltuid, string lToken,
        string gameUid, string region, Game game, string code)
        : base(userId, ltuid, lToken, gameUid, region)
    {
        Game = game;
        Code = code;
    }
}