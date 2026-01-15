#region

using Mehrak.Domain.Services.Abstractions;

#endregion

namespace Mehrak.Application.Models.Context;

public class CheckInApplicationContext : IApplicationContext
{
    public ulong UserId { get; }

    public ulong LtUid { get; set; }

    public string LToken { get; set; } = string.Empty;

    public CheckInApplicationContext(ulong userId)
    {
        UserId = userId;
    }

    public string? GetParameter(string key)
    {
        return default;
    }
}
