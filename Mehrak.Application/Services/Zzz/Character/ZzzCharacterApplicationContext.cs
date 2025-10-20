using Mehrak.Application.Models.Context;

namespace Mehrak.Application.Services.Zzz.Character;

public class ZzzCharacterApplicationContext(ulong userId, params (string, object)[] parameters)
    : ApplicationContextBase(userId, parameters)
{
}
