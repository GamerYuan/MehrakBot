using Mehrak.Application.Models.Context;

namespace Mehrak.Application.Services.Genshin.Abyss;

public class GenshinAbyssApplicationContext : ApplicationContextBase
{
    public GenshinAbyssApplicationContext(ulong userId,
        params (string, object)[] parameters)
        : base(userId, parameters)
    {
    }
}
