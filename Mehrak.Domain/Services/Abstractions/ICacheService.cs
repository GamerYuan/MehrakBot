using Mehrak.Domain.Models.Abstractions;

namespace Mehrak.Domain.Services.Abstractions;

public interface ICacheService
{
    public Task SetAsync<T>(ICacheEntry<T> entry);

    public Task<T?> GetAsync<T>(string key);

    public Task RemoveAsync(string key);
}
