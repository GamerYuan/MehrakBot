using Mehrak.Domain.Enums;
using Mehrak.GameApi.Common.Types;

namespace Mehrak.GameApi.Hsr.Types;

public class HsrEndGameApiContext : BaseHoYoApiContext
{
    public HsrEndGameMode GameMode { get; init; }

    public HsrEndGameApiContext(ulong userId, ulong ltuid, string lToken, string gameUid, string region,
        HsrEndGameMode gameMode)
        : base(userId, ltuid, lToken, gameUid, region)
    {
        GameMode = gameMode;
    }
}
