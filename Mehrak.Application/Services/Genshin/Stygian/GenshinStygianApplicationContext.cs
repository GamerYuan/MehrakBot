using Mehrak.Application.Models.Context;
using Mehrak.Domain.Enums;

namespace Mehrak.Application.Services.Genshin.Stygian;

public class GenshinStygianApplicationContext(ulong userId, ulong ltUid, string lToken, Server server,
    params (string, object)[] parameters)
    : ApplicationContextBase(userId, ltUid, lToken, server, parameters)
{
}
