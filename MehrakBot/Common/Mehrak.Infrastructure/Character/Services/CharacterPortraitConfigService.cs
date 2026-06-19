using System.Data;
using System.Text.Json;
using Mehrak.Domain.Character;
using Mehrak.Domain.Character.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Infrastructure.Character.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mehrak.Infrastructure.Character.Services;

internal class CharacterPortraitConfigService : ICharacterPortraitConfigService
{
    private readonly IServiceScopeFactory m_ScopeFactory;
    private readonly IDistributedCache m_Cache;
    private readonly ILogger<CharacterPortraitConfigService> m_Logger;

    public CharacterPortraitConfigService(IServiceScopeFactory scopeFactory, IDistributedCache cache, ILogger<CharacterPortraitConfigService> logger)
    {
        m_ScopeFactory = scopeFactory;
        m_Cache = cache;
        m_Logger = logger;
    }

    public async Task<CharacterPortraitConfig?> GetConfigAsync(Game game, int serverId)
    {
        try
        {
            var cacheKey = $"portrait_cfg_{game}_{serverId}";
            var cachedData = await m_Cache.GetStringAsync(cacheKey);

            if (!string.IsNullOrEmpty(cachedData))
            {
                try
                {
                    var cachedConfig = JsonSerializer.Deserialize<PortraitConfigCacheModel>(cachedData);
                    if (cachedConfig != null)
                        return ToConfig(cachedConfig);
                }
                catch (JsonException)
                {
                    await m_Cache.RemoveAsync(cacheKey);
                }
            }

            using var scope = m_ScopeFactory.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();
            var entity = await context.CharacterPortraitConfigs.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Game == game && c.ServerId == serverId);

            if (entity == null)
                return null;

            var cacheModel = ToCacheModel(entity);

            var cacheOptions = new DistributedCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromHours(1)
            };

            await m_Cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(cacheModel), cacheOptions);

            return ToConfig(entity);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Failed to get character portrait config for {Game} - ServerId {ServerId}", game, serverId);
            return null;
        }
    }

    public async Task<Dictionary<string, CharacterPortraitConfig>> GetAllConfigsAsync(Game game)
    {
        var cacheKey = $"portrait_cfg_all_{game}";
        var cachedData = await m_Cache.GetStringAsync(cacheKey);

        if (!string.IsNullOrEmpty(cachedData))
        {
            var cachedConfigs = JsonSerializer.Deserialize<Dictionary<string, PortraitConfigCacheModel>>(cachedData);
            if (cachedConfigs != null)
                return cachedConfigs.ToDictionary(k => k.Key, v => ToConfig(v.Value));
        }

        using var scope = m_ScopeFactory.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

        var entities = await context.CharacterPortraitConfigs.AsNoTracking()
            .Where(c => c.Game == game)
            .OrderBy(c => c.Name)
            .ThenBy(c => c.ServerId)
            .ToListAsync();

        var result = new Dictionary<string, CharacterPortraitConfig>(StringComparer.OrdinalIgnoreCase);
        var cacheModel = new Dictionary<string, PortraitConfigCacheModel>(StringComparer.OrdinalIgnoreCase);
        var nameCounts = entities.GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var entity in entities)
        {
            var key = nameCounts.GetValueOrDefault(entity.Name) > 1
                ? $"{entity.Name}_{entity.ServerId}"
                : entity.Name;

            result[key] = ToConfig(entity);
            cacheModel[key] = ToCacheModel(entity);
        }

        var cacheOptions = new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromHours(1)
        };

        await m_Cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(cacheModel), cacheOptions);

        return result;
    }

    public async Task<bool> UpsertConfigAsync(Game game, int serverId, CharacterPortraitConfigUpdate update)
    {
        const int maxRetries = 3;

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            using var scope = m_ScopeFactory.CreateScope();
            await using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();
            await using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                var serverIdEntity = await context.CharacterServerIds
                    .AsNoTracking()
                    .Include(x => x.Character)
                    .FirstOrDefaultAsync(x => x.Character.Game == game && x.ServerId == serverId);

                if (serverIdEntity == null)
                    return false;

                var characterName = serverIdEntity.Character.Name;

                var entity = await context.CharacterPortraitConfigs
                    .FirstOrDefaultAsync(c => c.Game == game && c.ServerId == serverId);

                if (entity == null)
                {
                    entity = new CharacterPortraitConfigModel
                    {
                        Game = game,
                        ServerId = serverId,
                        Name = characterName,
                        OffsetX = update.OffsetX,
                        OffsetY = update.OffsetY,
                        TargetScale = update.TargetScale,
                        FlipX = update.FlipX,
                        ArtistAttribution = update.ArtistAttribution
                    };
                    context.CharacterPortraitConfigs.Add(entity);
                }
                else
                {
                    entity.Name = characterName;
                    entity.OffsetX = update.OffsetX;
                    entity.OffsetY = update.OffsetY;
                    entity.TargetScale = update.TargetScale;
                    entity.FlipX = update.FlipX;
                    entity.ArtistAttribution = update.ArtistAttribution;
                }

                await context.SaveChangesAsync();
                await transaction.CommitAsync();

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var cacheKey = $"portrait_cfg_{game}_{serverId}";
                        var cacheModel = ToCacheModel(entity);

                        var cacheOptions = new DistributedCacheEntryOptions
                        {
                            SlidingExpiration = TimeSpan.FromHours(1)
                        };

                        await m_Cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(cacheModel), cacheOptions);

                        var allCacheKey = $"portrait_cfg_all_{game}";
                        await m_Cache.RemoveAsync(allCacheKey);
                    }
                    catch (Exception e)
                    {
                        m_Logger.LogError(e, "Failed to update cache for character portrait config after upsert.");
                    }
                });

                return true;
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync();

                if (attempt == maxRetries - 1)
                    throw;
            }
        }

        return false;
    }

    private static CharacterPortraitConfig ToConfig(CharacterPortraitConfigModel entity)
    {
        return new CharacterPortraitConfig
        {
            ServerId = entity.ServerId,
            OffsetX = entity.OffsetX,
            OffsetY = entity.OffsetY,
            TargetScale = entity.TargetScale,
            FlipX = entity.FlipX,
            ArtistAttribution = entity.ArtistAttribution
        };
    }

    private static CharacterPortraitConfig ToConfig(PortraitConfigCacheModel cache)
    {
        return new CharacterPortraitConfig
        {
            ServerId = cache.ServerId,
            OffsetX = cache.OffsetX,
            OffsetY = cache.OffsetY,
            TargetScale = cache.TargetScale,
            FlipX = cache.FlipX,
            ArtistAttribution = cache.ArtistAttribution
        };
    }

    private static PortraitConfigCacheModel ToCacheModel(CharacterPortraitConfigModel entity)
    {
        return new PortraitConfigCacheModel
        {
            ServerId = entity.ServerId,
            OffsetX = entity.OffsetX,
            OffsetY = entity.OffsetY,
            TargetScale = entity.TargetScale,
            FlipX = entity.FlipX,
            ArtistAttribution = entity.ArtistAttribution
        };
    }

    private sealed class PortraitConfigCacheModel
    {
        public int ServerId { get; set; }
        public int? OffsetX { get; set; }
        public int? OffsetY { get; set; }
        public float? TargetScale { get; set; }
        public bool? FlipX { get; set; }
        public string? ArtistAttribution { get; set; }
    }
}
