#region

using Mehrak.Application.Models.Context;

#endregion

namespace Mehrak.Application.Services.Genshin.Stygian;

public class GenshinStygianApplicationContext(
    ulong userId,
    params IEnumerable<(string, object)> parameters)
    : ApplicationContextBase(userId, parameters)
{
}