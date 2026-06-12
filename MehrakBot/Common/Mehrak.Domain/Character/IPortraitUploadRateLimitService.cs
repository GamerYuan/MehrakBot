namespace Mehrak.Domain.Character;

public interface IPortraitUploadRateLimitService
{
    Task<bool> IsAllowedAsync(long discordUserId, CancellationToken ct = default);
    Task<int> GetRemainingAsync(long discordUserId, CancellationToken ct = default);
}
