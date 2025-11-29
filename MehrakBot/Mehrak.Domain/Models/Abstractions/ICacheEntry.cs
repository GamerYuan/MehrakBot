namespace Mehrak.Domain.Models.Abstractions;

public interface ICacheEntry<out T>
{
    string Key { get; }
    T Value { get; }
    TimeSpan ExpirationTime { get; }
}