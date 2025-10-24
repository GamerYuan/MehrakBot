using Mehrak.Application.Models.Context;

namespace Mehrak.Application.Services.Hsr.Character;

public class HsrCharacterApplicationContext(ulong userId, params IEnumerable<(string, object)> param)
    : ApplicationContextBase(userId, param)
{
}
