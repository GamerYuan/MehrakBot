namespace Mehrak.Domain.Models.Abstractions;

public interface ICacheEntry<out T>
{
    public string Key { get; }
    public T Value { get; }
    public TimeSpan ExpirationTime { get; }
}