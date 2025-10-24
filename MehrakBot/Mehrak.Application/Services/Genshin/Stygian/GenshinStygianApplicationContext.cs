using Mehrak.Application.Models.Context;

namespace Mehrak.Application.Services.Genshin.Stygian;

public class GenshinStygianApplicationContext(ulong userId,
    params (string, object)[] parameters)
    : ApplicationContextBase(userId, parameters)
{
}
