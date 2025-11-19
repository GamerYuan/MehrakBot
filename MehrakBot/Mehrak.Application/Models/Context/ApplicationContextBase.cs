#region

using Mehrak.Domain.Services.Abstractions;

#endregion

namespace Mehrak.Application.Models.Context;

public class ApplicationContextBase : IApplicationContext
{
    public ulong UserId { get; }
    public ulong LtUid { get; set; }
    public string LToken { get; set; } = string.Empty;

    private Dictionary<string, object> Parameters { get; } = [];

    public ApplicationContextBase(ulong userId, params IEnumerable<(string, object)> parameters)
    {
        UserId = userId;
        foreach (var (key, value) in parameters) Parameters[key] = value;
    }

    public T? GetParameter<T>(string key)
    {
        return Parameters.TryGetValue(key, out var value) && value is T tValue ? tValue : default;
    }
}
