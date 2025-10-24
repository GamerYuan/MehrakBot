namespace Mehrak.Domain.Services.Abstractions;

public interface IApplicationContext
{
    public ulong UserId { get; }
    public ulong LtUid { get; set; }
    public string LToken { get; set; }

    public T? GetParameter<T>(string key);
}
