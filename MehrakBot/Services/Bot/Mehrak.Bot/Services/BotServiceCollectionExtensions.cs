#region

using Mehrak.Bot.Authentication;
using Mehrak.Bot.Builders;
using Mehrak.Bot.Provider;
using Mehrak.Bot.Services.Abstractions;
using Mehrak.Bot.Services.RateLimit;
using Microsoft.Extensions.DependencyInjection;

#endregion

namespace Mehrak.Bot.Services;

internal static class BotServiceCollectionExtensions
{
    internal static IServiceCollection AddBotServices(this IServiceCollection services)
    {
        services.AddCommandExecutorBuilder();

        services.AddSingleton<IAuthenticationMiddlewareService, AuthenticationMiddlewareService>();
        services.AddSingleton<ICommandRateLimitService, CommandRateLimitService>();

        services.AddSingleton<ICharacterAutocompleteService, CharacterAutocompleteService>();

        services.AddSingleton<IBotMetrics, BotMetricsService>();
        services.AddSingleton<UserCountTrackerService>();

        services.AddHostedService<BotRichStatusService>();

        services.AddSingleton<ClickhouseClientService>();

        services.AddHostedService<BotLatencyService>();

        return services;
    }
}
