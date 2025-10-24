using Mehrak.Application.Models.Context;

namespace Mehrak.Application.Services.Genshin.Character;

public class GenshinCharacterApplicationContext : ApplicationContextBase
{
    public GenshinCharacterApplicationContext(ulong userId, params (string, object)[] parameters)
        : base(userId, parameters)
    {
    }
}
