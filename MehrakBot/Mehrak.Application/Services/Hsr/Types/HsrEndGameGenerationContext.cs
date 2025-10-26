#region

using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.GameApi.Hsr.Types;

#endregion

namespace Mehrak.Application.Services.Hsr.Types;

public class HsrEndGameGenerationContext : ICardGenerationContext<HsrEndInformation>
{
    public ulong UserId { get; }
    public HsrEndInformation Data { get; }
    public Server Server { get; }
    public GameProfileDto GameProfile { get; }
    public HsrEndGameMode GameMode { get; }

    public HsrEndGameGenerationContext(ulong userId, HsrEndInformation data,
        Server server, GameProfileDto gameProfile, HsrEndGameMode gameMode)
    {
        UserId = userId;
        Data = data;
        Server = server;
        GameProfile = gameProfile;
        GameMode = gameMode;
    }
}