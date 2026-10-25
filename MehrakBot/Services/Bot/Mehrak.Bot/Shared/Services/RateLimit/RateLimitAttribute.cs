using Mehrak.Bot.Shared.Abstractions;
using Mehrak.Bot.Shared.Localization;
using Mehrak.Bot.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using NetCord.Services;

namespace Mehrak.Bot.Shared.Services.RateLimit;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
internal class RateLimitAttribute<TContext> : PreconditionAttribute<TContext> where TContext : IUserContext
{
    public override async ValueTask<PreconditionResult> EnsureCanExecuteAsync(TContext context, IServiceProvider? serviceProvider)
    {
        var localizer = serviceProvider?.GetService<ICommandUiLocalizer>() ?? CommandUiLocalizer.Default;
        var culture = context is IInteractionContext interactionContext
            ? BotUiCulture.Resolve(interactionContext)
            : BotUiCulture.English;

        try
        {
            if (serviceProvider == null)
                return PreconditionResult.Fail(localizer.Get(culture, CommandUiKeys.RateLimitUnavailable));

            var rateLimiter = serviceProvider.GetRequiredService<ICommandRateLimitService>();

            var isAllowed = await rateLimiter.IsAllowedAsync(context.User.Id);

            if (!isAllowed)
                return PreconditionResult.Fail(localizer.Get(culture, CommandUiKeys.RateLimitTooFrequent));
            return PreconditionResult.Success;
        }
        catch (Exception)
        {
            return PreconditionResult.Fail(localizer.Get(culture, CommandUiKeys.RateLimitUnavailable));
        }
    }
}
