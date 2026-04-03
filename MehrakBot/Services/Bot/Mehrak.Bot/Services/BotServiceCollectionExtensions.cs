#region

using Mehrak.Bot.Authentication;
using Mehrak.Bot.Builders;
using Mehrak.Bot.Provider;
using Mehrak.Bot.Services.Abstractions;
using Mehrak.Bot.Services.RateLimit;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Common.Types;
using Microsoft.Extensions.DependencyInjection;

#endregion

namespace Mehrak.Bot.Services;

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
