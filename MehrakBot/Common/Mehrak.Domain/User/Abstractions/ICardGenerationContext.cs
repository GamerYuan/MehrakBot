#region

#endregion

using Mehrak.Domain.User.Models;

namespace Mehrak.Domain.User.Abstractions;

public interface ICardGenerationContext<out T>
{
    ulong UserId { get; }
    T Data { get; }
    GameProfileDto GameProfile { get; }

    TParam? GetParameter<TParam>(string key);

    void SetParameter(string key, object value);
}

