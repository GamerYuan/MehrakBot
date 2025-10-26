#region

using Mehrak.Application.Models.Context;

#endregion

namespace Mehrak.Application.Services.Genshin.Abyss;

public class GenshinAbyssApplicationContext : ApplicationContextBase
{
    public GenshinAbyssApplicationContext(ulong userId,
        params IEnumerable<(string, object)> parameters)
        : base(userId, parameters)
    {
    }
}