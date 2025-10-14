using Mehrak.Application.Models.Context;
using Mehrak.Domain.Enums;

namespace Mehrak.Application.Services.Genshin.Character;

public class GenshinCharacterApplicationContext : ApplicationContextBase
{
    public GenshinCharacterApplicationContext(ulong userId, ulong ltUid, string lToken, Server server,
        params (string, object)[] parameters) : base(userId, ltUid, lToken, server, parameters)
    {
    }
}
