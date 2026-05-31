#region

using Mehrak.Domain.Cache.Abstractions;


#endregion

namespace Mehrak.GameApi.Shared.Types;

public class CharacterListCacheEntry<T> : ICacheEntry<IEnumerable<T>>
{
    public string Key { get; }
    public IEnumerable<T> Value { get; }
    public TimeSpan ExpirationTime { get; }

    public CharacterListCacheEntry(string key, IEnumerable<T> value, TimeSpan expirationTime)
    {
        Key = key;
        Value = value;
        ExpirationTime = expirationTime;
    }
}
