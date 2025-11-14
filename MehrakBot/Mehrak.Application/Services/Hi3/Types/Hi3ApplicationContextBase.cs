using Mehrak.Domain.Enums;
using Mehrak.Domain.Services.Abstractions;

namespace Mehrak.Application.Services.Hi3.Types;

public class Hi3ApplicationContextBase : IApplicationContext
{
    public ulong UserId { get; }
    public ulong LtUid { get; set; }
    public string LToken { get; set; } = string.Empty;
    public Hi3Server Server { get; set; }

    private Dictionary<string, object> Parameters { get; } = [];

    public Hi3ApplicationContextBase(ulong userId, params IEnumerable<(string, object)> parameters)
    {
        UserId = userId;
        foreach (var (key, value) in parameters) Parameters[key] = value;
    }

    public T? GetParameter<T>(string key)
    {
        return Parameters.TryGetValue(key, out var value) && value is T tValue ? tValue : default;
    }
}
