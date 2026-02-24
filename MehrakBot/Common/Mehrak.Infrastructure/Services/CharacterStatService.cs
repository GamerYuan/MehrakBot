using System.Text.Json;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace Mehrak.Infrastructure.Services;

internal class CharacterStatService : ICharacterStatService
{
    private readonly IServiceScopeFactory m_ScopeFactory;
    private readonly IDistributedCache m_Cache;

    public CharacterStatService(IServiceScopeFactory scopeFactory, IDistributedCache cache)
    {
        m_ScopeFactory = scopeFactory;
        m_Cache = cache;
    }

    public async Task<(float? BaseVal, float? MaxAscVal)> GetCharAscStatAsync(string characterName)
    {
        var cacheKey = $"char_stat_{characterName}";
        var cachedData = await m_Cache.GetStringAsync(cacheKey);

        if (!string.IsNullOrEmpty(cachedData))
        {
            var cachedStat = JsonSerializer.Deserialize<CharacterStatCacheModel>(cachedData);
            if (cachedStat != null)
            {
                return (cachedStat.BaseVal, cachedStat.MaxAscVal);
            }
        }

        using var scope = m_ScopeFactory.CreateScope();
        using var characterContext = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();
        var charDto = await characterContext.Characters.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Name == characterName);

        if (charDto != null)
        {
            var cacheModel = new CharacterStatCacheModel
            {
                BaseVal = charDto.BaseVal,
                MaxAscVal = charDto.MaxAscVal
            };

            var cacheOptions = new DistributedCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromHours(1)
            };

            await m_Cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(cacheModel), cacheOptions);

            return (charDto.BaseVal, charDto.MaxAscVal);
        }

        return (null, null);
    }

    public async Task<Dictionary<string, (float? BaseVal, float? MaxAscVal)>> GetAllCharAscStatsAsync(Game game)
    {
        var cacheKey = $"all_char_stats_{game}";
        var cachedData = await m_Cache.GetStringAsync(cacheKey);

        if (!string.IsNullOrEmpty(cachedData))
        {
            var cachedStats = JsonSerializer.Deserialize<Dictionary<string, CharacterStatCacheModel>>(cachedData);
            if (cachedStats != null)
            {
                return cachedStats.ToDictionary(k => k.Key, v => (v.Value.BaseVal, v.Value.MaxAscVal));
            }
        }

        using var scope = m_ScopeFactory.CreateScope();
        using var characterContext = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

        var chars = await characterContext.Characters.AsNoTracking()
            .Where(c => c.Game == game)
            .ToListAsync();

        var result = chars.ToDictionary(c => c.Name, c => (c.BaseVal, c.MaxAscVal));

        var cacheModel = chars.ToDictionary(c => c.Name, c => new CharacterStatCacheModel
        {
            BaseVal = c.BaseVal,
            MaxAscVal = c.MaxAscVal
        });

        var cacheOptions = new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromHours(1)
        };

        await m_Cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(cacheModel), cacheOptions);

        return result;
    }

    public async Task<bool> UpdateCharAscStatAsync(Game game, string characterName, float? baseVal, float? maxAscVal)
    {
        using var scope = m_ScopeFactory.CreateScope();
        using var characterContext = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

        var charModel = await characterContext.Characters
            .FirstOrDefaultAsync(c => c.Game == game && c.Name == characterName);

        if (charModel == null)
            return false;

        charModel.BaseVal = baseVal;
        charModel.MaxAscVal = maxAscVal;

        await characterContext.SaveChangesAsync();

        var cacheKey = $"char_stat_{characterName}";
        var cacheModel = new CharacterStatCacheModel
        {
            BaseVal = baseVal,
            MaxAscVal = maxAscVal
        };

        var cacheOptions = new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromHours(1)
        };

        await m_Cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(cacheModel), cacheOptions);

        var allStatsCacheKey = $"all_char_stats_{game}";
        await m_Cache.RemoveAsync(allStatsCacheKey);

        return true;
    }

    private sealed class CharacterStatCacheModel
    {
        public float? BaseVal { get; set; }
        public float? MaxAscVal { get; set; }
    }
}
