namespace Mehrak.Domain.Services.Abstractions;

public interface ICommandRateLimitService
{
    public Task<bool> IsRateLimitedAsync(ulong userId);

    public Task SetRateLimitAsync(ulong userId);
}