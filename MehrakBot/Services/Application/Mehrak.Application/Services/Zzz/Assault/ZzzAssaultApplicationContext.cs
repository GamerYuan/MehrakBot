#region

using Mehrak.Application.Models.Context;

#endregion

namespace Mehrak.Application.Services.Zzz.Assault;

public class ZzzAssaultApplicationContext(ulong userId, params IEnumerable<(string, string)> parameters)
    : ApplicationContextBase(userId, parameters)
{
}
