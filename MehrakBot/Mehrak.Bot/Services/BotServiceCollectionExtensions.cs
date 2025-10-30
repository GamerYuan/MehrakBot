#region

using Mehrak.Application.Models.Context;
using Mehrak.Bot.Authentication;
using Mehrak.Bot.Builders;
using Mehrak.Bot.Provider;
using Mehrak.Bot.Services.Autocomplete;
using Mehrak.Domain.Services.Abstractions;
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

        services.AddTransient<ICommandExecutorService<CheckInApplicationContext>, CheckInExecutorService>();

        services.AddHostedService<AsyncInitializationHostedService>();

        return services;
    }
}
