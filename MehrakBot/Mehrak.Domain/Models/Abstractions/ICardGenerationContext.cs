#region

using Mehrak.Domain.Enums;

#endregion

namespace Mehrak.Domain.Models.Abstractions;

public interface ICardGenerationContext<out T>
{
    public ulong UserId { get; }
    public T Data { get; }
    public Server Server { get; }
    public GameProfileDto GameProfile { get; }
}