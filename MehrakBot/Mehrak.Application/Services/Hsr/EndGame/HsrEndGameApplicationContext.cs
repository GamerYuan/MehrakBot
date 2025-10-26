#region

using Mehrak.Application.Models.Context;
using Mehrak.Domain.Enums;

#endregion

namespace Mehrak.Application.Services.Hsr.EndGame;

public class HsrEndGameApplicationContext : ApplicationContextBase
{
    public HsrEndGameMode Mode { get; }

    public HsrEndGameApplicationContext(ulong userId, HsrEndGameMode mode,
        params IEnumerable<(string, object)> parameters) : base(userId, parameters)
    {
        Mode = mode;
    }
}