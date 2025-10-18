using Mehrak.Application.Models.Context;

namespace Mehrak.Application.Services.Genshin.Theater;

public class GenshinTheaterApplicationContext(ulong userId,
    params (string, object)[] parameters)
    : ApplicationContextBase(userId, parameters)
{
}
