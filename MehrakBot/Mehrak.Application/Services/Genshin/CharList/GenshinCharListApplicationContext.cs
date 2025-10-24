using Mehrak.Application.Models.Context;

namespace Mehrak.Application.Services.Genshin.CharList;

public class GenshinCharListApplicationContext(ulong userId,
    params IEnumerable<(string, object)> parameters) :
    ApplicationContextBase(userId, parameters)
{
}
