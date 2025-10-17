using Mehrak.Domain.Services.Abstractions;
using Mehrak.Infrastructure.Models;
using Microsoft.Extensions.Logging;

namespace Mehrak.Bot.Services;

internal class CommandRateLimitService : ICommandRateLimitService
{
    private readonly ICacheService m_CacheService;
    private readonly ILogger<CommandRateLimitService> m_Logger;

    private static readonly TimeSpan DefaultExpirationTime = TimeSpan.FromSeconds(10);

    public CommandRateLimitService(ICacheService cacheService, ILogger<CommandRateLimitService> logger)
    {
        m_CacheService = cacheService;
        m_Logger = logger;
    }

    public async Task<bool> IsRateLimitedAsync(ulong userId)
    {
        string cacheKey = $"cmd_rate_limit:{userId}";
        string? val = await m_CacheService.GetAsync<string>(cacheKey);

        if (val == null)
        {
            return false;
        }

        m_Logger.LogDebug("User {UserId} is rate limited", userId);
        return true;
    }

    public async Task SetRateLimitAsync(ulong userId)
    {
        string cacheKey = $"cmd_rate_limit:{userId}";
        await m_CacheService.SetAsync(new CacheEntryBase<string>(cacheKey, "1", DefaultExpirationTime));
        m_Logger.LogDebug("Set rate limit for user {UserId}", userId);
    }
}
