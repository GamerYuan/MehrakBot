using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;

namespace Mehrak.Application.Services.Hi3.Types;

internal class Hi3CardGenerationContext<T> : ICardGenerationContext<T>
{
    public ulong UserId { get; }

    public T Data { get; }

    public GameProfileDto GameProfile { get; }

    private readonly Dictionary<string, object> m_Params = [];

    public Hi3CardGenerationContext(ulong userId, T data, Hi3Server server, GameProfileDto gameProfile)
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
