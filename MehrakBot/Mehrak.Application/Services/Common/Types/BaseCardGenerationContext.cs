#region

using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;

#endregion

namespace Mehrak.Application.Services.Common.Types;

public class BaseCardGenerationContext<T> : ICardGenerationContext<T>
{
    public ulong UserId { get; }

    public T Data { get; }

    public GameProfileDto GameProfile { get; }

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
