using Mehrak.Domain.Enums;

namespace Mehrak.Application.Models.Context;

public class CodeRedeemApplicationContext : ApplicationContextBase
{
    public Game Game { get; }

    public CodeRedeemApplicationContext(ulong userId, Game game, params IEnumerable<(string, object)> param)
        : base(userId, param)
    {
        Game = game;
    }
}
