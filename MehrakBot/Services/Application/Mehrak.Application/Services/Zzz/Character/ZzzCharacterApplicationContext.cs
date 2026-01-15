#region

using Mehrak.Application.Models.Context;

#endregion

namespace Mehrak.Application.Services.Zzz.Character;

public class ZzzCharacterApplicationContext(ulong userId, params IEnumerable<(string, string)> parameters)
    : ApplicationContextBase(userId, parameters)
{
}
