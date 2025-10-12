using Mehrak.Domain.Enums;

namespace Mehrak.Domain.Models.Abstractions;

public interface ICardGenerationContext<T>
{
    public ulong UserId { get; }
    public T Data { get; }
    public Server Server { get; }
    public GameProfileDto GameProfile { get; }
}
