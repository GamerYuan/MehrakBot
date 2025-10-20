using Mehrak.Application.Models.Context;

namespace Mehrak.Application.Services.Hsr.Memory;

public class HsrMemoryApplicationContext(ulong userId, params (string, object)[] parameters)
    : ApplicationContextBase(userId, parameters)
{
}
