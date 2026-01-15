namespace Mehrak.Domain.Services.Abstractions;

public interface ICommandRateLimitService
{
    Task<bool> IsRateLimitedAsync(ulong userId);

    Task SetRateLimitAsync(ulong userId);
}