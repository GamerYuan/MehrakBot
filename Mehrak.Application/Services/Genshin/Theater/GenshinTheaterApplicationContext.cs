using Mehrak.Application.Models.Context;
using Mehrak.Domain.Enums;

namespace Mehrak.Application.Services.Genshin.Theater;

public class GenshinTheaterApplicationContext(ulong userId,
    Server server, params (string, object)[] parameters)
    : ApplicationContextBase(userId, server, parameters)
{
}
