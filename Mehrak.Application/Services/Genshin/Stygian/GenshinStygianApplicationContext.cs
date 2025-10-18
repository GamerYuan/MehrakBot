using Mehrak.Application.Models.Context;
using Mehrak.Domain.Enums;

namespace Mehrak.Application.Services.Genshin.Stygian;

public class GenshinStygianApplicationContext(ulong userId, Server server,
    params (string, object)[] parameters)
    : ApplicationContextBase(userId, server, parameters)
{
}
