#region

using Mehrak.Application.Models.Context;

#endregion

namespace Mehrak.Application.Services.Hsr.Memory;

public class HsrMemoryApplicationContext(ulong userId, params IEnumerable<(string, object)> parameters)
    : ApplicationContextBase(userId, parameters)
{
}