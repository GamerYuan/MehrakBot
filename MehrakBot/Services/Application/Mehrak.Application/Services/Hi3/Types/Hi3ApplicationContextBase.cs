using Mehrak.Domain.Services.Abstractions;

namespace Mehrak.Application.Services.Hi3.Types;

public class Hi3ApplicationContextBase : IApplicationContext
{
    public ulong UserId { get; }
    public ulong LtUid { get; set; }
    public string LToken { get; set; } = string.Empty;

    private Dictionary<string, string> Parameters { get; } = [];

    public Hi3ApplicationContextBase(ulong userId, params IEnumerable<(string, string)> parameters)
    {
        UserId = userId;
        foreach (var (key, value) in parameters) Parameters[key] = value;
    }

    public string? GetParameter(string key)
    {
        return Parameters.GetValueOrDefault(key);
    }
}
