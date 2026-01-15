#region

using Mehrak.Application.Models.Context;

#endregion

namespace Mehrak.Application.Services.Hsr.Character;

public class HsrCharacterApplicationContext(ulong userId, params IEnumerable<(string, string)> param)
    : ApplicationContextBase(userId, param)
{
}
