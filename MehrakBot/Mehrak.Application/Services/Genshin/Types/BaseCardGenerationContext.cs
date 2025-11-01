#region

using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;

#endregion

namespace Mehrak.Application.Services.Genshin.Types;

public class BaseCardGenerationContext<T> : ICardGenerationContext<T>
{
    public ulong UserId { get; }

    public T Data { get; }

    public Server Server { get; }

    public GameProfileDto GameProfile { get; }

    public BaseCardGenerationContext(ulong userId, T data, Server server, GameProfileDto gameProfile)
    {
        UserId = userId;
        Data = data;
        Server = server;
        GameProfile = gameProfile;
    }
}