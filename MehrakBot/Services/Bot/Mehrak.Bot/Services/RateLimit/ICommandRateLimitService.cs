namespace Mehrak.Bot.Services.RateLimit;

public interface ICommandRateLimitService
{
    /// <summary>
    /// Check if user is allowed to make request
    /// </summary>
    /// <param name="userId">Discord UserId to check for rate limit</param>
    /// <returns>true if allowed, false if not allowed (rate limited)</returns>
    Task<bool> IsAllowedAsync(ulong userId);
}
