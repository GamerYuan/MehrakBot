#region

#endregion

namespace Mehrak.Domain.Models.Abstractions;

public interface ICardGenerationContext<out T>
{
    ulong UserId { get; }
    T Data { get; }
    GameProfileDto GameProfile { get; }

    TParam? GetParameter<TParam>(string key);
}

