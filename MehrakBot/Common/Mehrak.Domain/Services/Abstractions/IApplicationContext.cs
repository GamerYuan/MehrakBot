namespace Mehrak.Domain.Services.Abstractions;

public interface IApplicationContext
{
    ulong UserId { get; }
    ulong LtUid { get; set; }
    string LToken { get; set; }

    T? GetParameter<T>(string key);

    void SetParameter<T>(string key, T value);
}
