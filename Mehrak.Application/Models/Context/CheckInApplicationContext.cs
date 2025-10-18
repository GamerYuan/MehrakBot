using Mehrak.Domain.Services.Abstractions;

namespace Mehrak.Application.Models.Context;

public class CheckInApplicationContext : IApplicationContext
{
    public ulong UserId { get; }

    public ulong LtUid { get; set; }

    public string LToken { get; set; }

    public CheckInApplicationContext(ulong userId, ulong ltuid, string ltoken)
    {
        UserId = userId;
        LtUid = ltuid;
        LToken = ltoken;
    }

    public T? GetParameter<T>(string key)
    {
        return default;
    }
}
