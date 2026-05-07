using System.Data;
using System.Text.Json;
using Amazon.Runtime.Internal.Util;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mehrak.Infrastructure.Services;

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

    public async Task<CharacterPortraitConfig?> GetConfigAsync(Game game, string characterName)
    {
        try
        {
            var cacheKey = $"portrait_cfg_{game}_{characterName}";
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
                .FirstOrDefaultAsync(c => c.Game == game && c.Name == characterName);

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
            m_Logger.LogError(e, "Failed to get character portrait config for {Game} - {CharacterName}", game, characterName);
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
            .ToListAsync();

        var result = entities.ToDictionary(e => e.Name, ToConfig);

        var cacheModel = entities.ToDictionary(e => e.Name, ToCacheModel);

        var cacheOptions = new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromHours(1)
        };

        await m_Cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(cacheModel), cacheOptions);

        return result;
    }

    public async Task UpsertConfigAsync(Game game, string characterName, CharacterPortraitConfigUpdate update)
    {
        const int maxRetries = 3;

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            using var scope = m_ScopeFactory.CreateScope();
            await using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                var entity = await context.CharacterPortraitConfigs
                    .FirstOrDefaultAsync(c => c.Game == game && c.Name == characterName);

                if (entity == null)
                {
                    entity = new CharacterPortraitConfigModel
                    {
                        Game = game,
                        Name = characterName,
                        OffsetX = update.OffsetX,
                        OffsetY = update.OffsetY,
                        TargetScale = update.TargetScale,
                        EnableGradientFade = update.EnableGradientFade,
                        GradientFadeStart = update.GradientFadeStart
                    };
                    context.CharacterPortraitConfigs.Add(entity);
                }
                else
                {
                    if (update.OffsetX.HasValue) entity.OffsetX = update.OffsetX;
                    if (update.OffsetY.HasValue) entity.OffsetY = update.OffsetY;
                    if (update.TargetScale.HasValue) entity.TargetScale = update.TargetScale;
                    if (update.EnableGradientFade.HasValue) entity.EnableGradientFade = update.EnableGradientFade;
                    if (update.GradientFadeStart.HasValue) entity.GradientFadeStart = update.GradientFadeStart;
                }

                await context.SaveChangesAsync();
                await transaction.CommitAsync();

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var cacheKey = $"portrait_cfg_{game}_{characterName}";
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

                return;
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync();

                if (attempt == maxRetries - 1)
                    throw;
            }
        }
    }

    private static CharacterPortraitConfig ToConfig(CharacterPortraitConfigModel entity)
    {
        return new CharacterPortraitConfig
        {
            OffsetX = entity.OffsetX,
            OffsetY = entity.OffsetY,
            TargetScale = entity.TargetScale,
            EnableGradientFade = entity.EnableGradientFade,
            GradientFadeStart = entity.GradientFadeStart
        };
    }

    private static CharacterPortraitConfig ToConfig(PortraitConfigCacheModel cache)
    {
        return new CharacterPortraitConfig
        {
            OffsetX = cache.OffsetX,
            OffsetY = cache.OffsetY,
            TargetScale = cache.TargetScale,
            EnableGradientFade = cache.EnableGradientFade,
            GradientFadeStart = cache.GradientFadeStart
        };
    }

    private static PortraitConfigCacheModel ToCacheModel(CharacterPortraitConfigModel entity)
    {
        return new PortraitConfigCacheModel
        {
            OffsetX = entity.OffsetX,
            OffsetY = entity.OffsetY,
            TargetScale = entity.TargetScale,
            EnableGradientFade = entity.EnableGradientFade,
            GradientFadeStart = entity.GradientFadeStart
        };
    }

    private sealed class PortraitConfigCacheModel
    {
        public int? OffsetX { get; set; }
        public int? OffsetY { get; set; }
        public float? TargetScale { get; set; }
        public bool? EnableGradientFade { get; set; }
        public float? GradientFadeStart { get; set; }
    }
}
