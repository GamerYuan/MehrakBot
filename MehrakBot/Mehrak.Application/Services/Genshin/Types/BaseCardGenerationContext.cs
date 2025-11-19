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

    public GameProfileDto GameProfile { get; }

    private readonly Dictionary<string, object> m_Params = [];

    public BaseCardGenerationContext(ulong userId, T data, Server server, GameProfileDto gameProfile)
    {
        UserId = userId;
        Data = data;
        GameProfile = gameProfile;

        m_Params.Add("server", server);
    }

    public TParam? GetParameter<TParam>(string key)
    {
        return m_Params.TryGetValue(key, out var value) && value is TParam param ? param : default;
    }
}
