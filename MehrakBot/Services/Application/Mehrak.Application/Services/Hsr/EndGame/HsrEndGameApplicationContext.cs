#region

using Mehrak.Application.Models.Context;

#endregion

namespace Mehrak.Application.Services.Hsr.EndGame;

public class HsrEndGameApplicationContext : ApplicationContextBase
{

    public HsrEndGameApplicationContext(ulong userId, params IEnumerable<(string, string)> parameters)
        : base(userId, parameters)
    {
    }
}
