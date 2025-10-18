using Mehrak.Application.Models.Context;
using Mehrak.Domain.Enums;

namespace Mehrak.Application.Services.Genshin.Abyss;

public class GenshinAbyssApplicationContext : ApplicationContextBase
{
    public GenshinAbyssApplicationContext(ulong userId, Server server,
        params (string, object)[] parameters)
        : base(userId, server, parameters)
    {
    }
}
