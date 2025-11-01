#region

using Mehrak.Application.Models.Context;

#endregion

namespace Mehrak.Application.Services.Zzz.Defense;

public class ZzzDefenseApplicationContext(ulong userId, params IEnumerable<(string, object)> parameters)
    : ApplicationContextBase(userId, parameters)
{
}