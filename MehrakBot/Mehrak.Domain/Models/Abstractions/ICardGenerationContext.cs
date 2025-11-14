#region

using Mehrak.Domain.Enums;

#endregion

namespace Mehrak.Domain.Models.Abstractions;

public interface ICardGenerationContext<out T, out TServer> where TServer : Enum
{
    ulong UserId { get; }
    T Data { get; }
    TServer Server { get; }
    GameProfileDto GameProfile { get; }
}

public interface ICardGenerationContext<out T> : ICardGenerationContext<T, Server>;


