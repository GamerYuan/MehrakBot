#region

using Mehrak.Application.Shared.Abstractions;


#endregion

namespace Mehrak.Application.Shared.Models;

public class ApplicationContextBase : IApplicationContext
{
    public ulong UserId { get; }
    public ulong LtUid { get; set; }
    public string LToken { get; set; } = string.Empty;

    private Dictionary<string, string> Parameters { get; } = [];

    public ApplicationContextBase(ulong userId, params IEnumerable<(string, string)> parameters)
    {
        UserId = userId;
        foreach (var (key, value) in parameters) Parameters[key] = value;
    }

    public string? GetParameter(string key)
    {
        return Parameters.GetValueOrDefault(key);
    }
}
