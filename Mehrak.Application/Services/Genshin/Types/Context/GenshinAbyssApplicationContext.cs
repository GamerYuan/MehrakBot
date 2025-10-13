using Mehrak.Application.Models.Context;
using Mehrak.Domain.Enums;

namespace Mehrak.Application.Services.Genshin.Types.Context;

public class GenshinAbyssApplicationContext : ApplicationContextBase
{
    public int Floor { get; }

    public GenshinAbyssApplicationContext(ulong userId, ulong ltUid, string lToken, string gameUid, Server server,
        int floor, params (string, object)[] parameters)
        : base(userId, ltUid, lToken, gameUid, server, parameters)
    {
        Floor = floor;
    }
}
