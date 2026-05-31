#region

using Mehrak.Bot.HylEmbed;
using Mehrak.Bot.Shared.Abstractions;
using Mehrak.Bot.Shared.Builders;
using Mehrak.Bot.Shared.Services;
using Mehrak.Bot.Shared.Services.RateLimit;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Hoyolab;
using Microsoft.Extensions.DependencyInjection;

#endregion

namespace Mehrak.Bot;

internal static class BotServiceCollectionExtensions
{
    internal static IServiceCollection AddBotServices(this IServiceCollection services)
    {
        services.AddLocalization();
        services.AddCommandExecutorBuilder();

        services.AddKeyedTransient<IBotService, HylEmbedService>(nameof(HylEmbedService));

        services.AddSingleton<IAuthenticationMiddlewareService, AuthenticationMiddlewareService>();
        services.AddSingleton<ICommandRateLimitService, CommandRateLimitService>();

        services.AddSingleton<ICharacterAutocompleteService, CharacterAutocompleteService>();
        services.AddSingleton<IBotLocalizationService, BotLocalizationService>();

        services.AddSingleton<IBotMetrics, BotMetricsService>();
        services.AddSingleton<UserCountTrackerService>();

        services.AddHostedService<BotRichStatusService>();

        services.AddSingleton<ClickhouseClientService>();

        services.AddHostedService<BotLatencyService>();


        // Bot specific game api services
        services.AddSingleton<IApiService<HylPost, HylPostApiContext>, HylPostApiService>();

        return services;
    }
}
