using Mehrak.Domain.Enums;
using Mehrak.Domain.Services.Abstractions;

namespace Mehrak.Application.Models.Context;

public class ApplicationContextBase : IApplicationContext
{
    public ulong UserId { get; }
    public ulong LtUid { get; }
    public string LToken { get; }
    public string GameUid { get; }
    public Server Server { get; }

    private Dictionary<string, object> Parameters { get; } = [];

    public ApplicationContextBase(ulong userId, ulong ltUid, string lToken, string gameUid, Server server, params (string, object)[] parameters)
    {
        UserId = userId;
        LtUid = ltUid;
        LToken = lToken;
        GameUid = gameUid;
        Server = server;
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
