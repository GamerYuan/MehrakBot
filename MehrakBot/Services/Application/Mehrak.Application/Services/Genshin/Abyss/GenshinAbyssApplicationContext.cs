#region

using Mehrak.Application.Models.Context;

#endregion

namespace Mehrak.Application.Services.Genshin.Abyss;

public class GenshinAbyssApplicationContext : ApplicationContextBase
{
    public GenshinAbyssApplicationContext(ulong userId,
        params IEnumerable<(string, string)> parameters)
        : base(userId, parameters)
    {
    }
}
