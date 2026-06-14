#region

using Mehrak.Domain.Character.Models;
using Mehrak.Domain.User.Abstractions;
using Mehrak.Domain.User.Models;

#endregion

namespace Mehrak.Application.Shared.Services.Types;

public class BaseCardGenerationContext<T> : ICardGenerationContext<T>
{
    public ulong UserId { get; }

    public T Data { get; }

    public GameProfileDto GameProfile { get; }

    public string? PortraitImageKey { get; set; }

    public CharacterPortraitConfig? PortraitConfig { get; set; }

    private readonly Dictionary<string, object> m_Params = [];

    public BaseCardGenerationContext(ulong userId, T data, GameProfileDto gameProfile)
    {
        UserId = userId;
        Data = data;
        GameProfile = gameProfile;
    }

    public TParam? GetParameter<TParam>(string key)
    {
        return m_Params.TryGetValue(key, out var value) && value is TParam param ? param : default;
    }

    public void SetParameter(string key, object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        m_Params.TryAdd(key, value);
    }
}
