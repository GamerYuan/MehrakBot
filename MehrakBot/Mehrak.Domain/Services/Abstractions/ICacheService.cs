#region

using Mehrak.Domain.Models.Abstractions;

#endregion

namespace Mehrak.Domain.Services.Abstractions;

public interface ICacheService
{
    Task SetAsync<T>(ICacheEntry<T> entry);

    Task<T?> GetAsync<T>(string key);

    Task RemoveAsync(string key);
}