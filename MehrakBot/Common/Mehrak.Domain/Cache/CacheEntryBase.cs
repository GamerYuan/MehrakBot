using Mehrak.Domain.Cache.Abstractions;

namespace Mehrak.Domain.Cache;

public class CacheEntryBase<T> : ICacheEntry<T>
{
    public string Key { get; }

    public T Value { get; }

    public TimeSpan ExpirationTime { get; }

    public CacheEntryBase(string key, T value, TimeSpan expirationTime)
    {
        Key = key;
        Value = value;
        ExpirationTime = expirationTime;
    }
}
