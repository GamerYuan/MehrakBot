#region

#endregion

using Mehrak.Domain.Character.Models;
using Mehrak.Domain.User.Models;

namespace Mehrak.Domain.User.Abstractions;

public interface ICardGenerationContext<out T>
{
    ulong UserId { get; }
    T Data { get; }
    GameProfileDto GameProfile { get; }

    Stream? PortraitImageStream { get; }
    CharacterPortraitConfig? PortraitConfig { get; }

    TParam? GetParameter<TParam>(string key);

    void SetParameter(string key, object value);
}

