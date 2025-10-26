#region

using Mehrak.Application.Models.Context;

#endregion

namespace Mehrak.Application.Services.Hsr.CharList;

public class HsrCharListApplicationContext(ulong userId, params IEnumerable<(string, object)> parameters)
    : ApplicationContextBase(userId, parameters)
{
}