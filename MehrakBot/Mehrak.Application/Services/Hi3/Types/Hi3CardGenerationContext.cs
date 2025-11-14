using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;

namespace Mehrak.Application.Services.Hi3.Types;

internal class Hi3CardGenerationContext<T> : ICardGenerationContext<T, Hi3Server>
{
    public ulong UserId { get; }

    public T Data { get; }

    public Hi3Server Server { get; }

    public GameProfileDto GameProfile { get; }

    public Hi3CardGenerationContext(ulong userId, T data, Hi3Server server, GameProfileDto gameProfile)
    {
        UserId = userId;
        Data = data;
        Server = server;
        GameProfile = gameProfile;
    }
}
