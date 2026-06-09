namespace Mehrak.Application.Shared.Abstractions;

public interface IApplicationContext
{
    ulong UserId { get; }
    ulong LtUid { get; set; }
    string LToken { get; set; }

    string? GetParameter(string key);
}
