using Mehrak.Domain.Services.Abstractions;

namespace Mehrak.Application.Models.Context;

public class ApplicationContextBase : IApplicationContext
{
    public ulong UserId { get; }
    public ulong LtUid { get; }
    public string LToken { get; }

    private Dictionary<string, object> Parameters { get; } = [];

    public ApplicationContextBase(ulong userId, ulong ltUid, string lToken, params (string, object)[] parameters)
    {
        UserId = userId;
        LtUid = ltUid;
        LToken = lToken;
        foreach (var (key, value) in parameters)
        {
            Parameters[key] = value;
        }
    }

    public T? GetParameter<T>(string key)
    {
        return Parameters.TryGetValue(key, out var value) && value is T tValue ? tValue : default;
    }
}
