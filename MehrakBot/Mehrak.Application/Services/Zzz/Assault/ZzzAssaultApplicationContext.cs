using Mehrak.Application.Models.Context;

namespace Mehrak.Application.Services.Zzz.Assault;

public class ZzzAssaultApplicationContext(ulong userId, params IEnumerable<(string, object)> parameters)
    : ApplicationContextBase(userId, parameters)
{
}
