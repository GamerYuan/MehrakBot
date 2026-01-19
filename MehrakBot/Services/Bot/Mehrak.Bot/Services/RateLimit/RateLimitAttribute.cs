using Microsoft.Extensions.DependencyInjection;
using NetCord.Services;

namespace Mehrak.Bot.Services.RateLimit;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
internal class RateLimitAttribute<TContext> : PreconditionAttribute<TContext> where TContext : IUserContext
{
    public override async ValueTask<PreconditionResult> EnsureCanExecuteAsync(TContext context, IServiceProvider? serviceProvider)
    {
        if (serviceProvider == null)
            return PreconditionResult.Fail("Rate limiting is temporarily unavailable. Please try again later.");

        var rateLimiter = serviceProvider.GetRequiredService<ICommandRateLimitService>();

        var isAllowed = await rateLimiter.IsAllowedAsync(context.User.Id);

        if (!isAllowed) return PreconditionResult.Fail("Used command too frequently! Please try again later");
        return PreconditionResult.Success;
    }
}
