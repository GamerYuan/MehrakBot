#region

using Mehrak.Application.Services.Abstractions;


#endregion

namespace Mehrak.Application.Models.Context;

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
