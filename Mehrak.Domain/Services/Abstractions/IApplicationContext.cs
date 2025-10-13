namespace Mehrak.Domain.Services.Abstractions;

public interface IApplicationContext
{
    public ulong UserId { get; }
    public ulong LtUid { get; }
    public string LToken { get; }

    public T? GetParameter<T>(string key);
}
