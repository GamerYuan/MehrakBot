using Mehrak.Application.Models.Context;

namespace Mehrak.Application.Services.Hsr.CharList;

public class HsrCharListApplicationContext(ulong userId, params (string, object)[] parameters)
    : ApplicationContextBase(userId, parameters)
{
}
