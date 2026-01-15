#region

using Mehrak.Application.Models.Context;

#endregion

namespace Mehrak.Application.Services.Genshin.Stygian;

public class GenshinStygianApplicationContext(
    ulong userId,
    params IEnumerable<(string, string)> parameters)
    : ApplicationContextBase(userId, parameters)
{
}
