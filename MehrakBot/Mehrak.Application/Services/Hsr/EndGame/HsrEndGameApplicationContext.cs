using Mehrak.Application.Models.Context;
using Mehrak.Domain.Enums;

namespace Mehrak.Application.Services.Hsr.EndGame;

public class HsrEndGameApplicationContext : ApplicationContextBase
{
    public HsrEndGameMode Mode { get; }

    public HsrEndGameApplicationContext(ulong userId, HsrEndGameMode mode,
        params (string, object)[] parameters) : base(userId, parameters)
    {
        Mode = mode;
    }
}
