#region

using MehrakCore.Repositories;
using MehrakCore.Services;
using MehrakCore.Services.Genshin;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Hosting.Services.ComponentInteractions;
using NetCord.Services.ComponentInteractions;

#endregion

namespace MehrakCore;

internal class Program
{
    private static async Task Main(string[] args)
    {
        HostApplicationBuilderSettings settings = new()
        {
            Args = args,
            Configuration = new ConfigurationManager(),
            ContentRootPath = Directory.GetCurrentDirectory()
        };

        settings.Configuration.AddJsonFile("appsettings.json")
            .AddUserSecrets<Program>()
            .AddEnvironmentVariables()
            .Build();

        var builder = Host.CreateApplicationBuilder(args);

        // Configure logging
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddDebug();

        // Configure minimum log level from configuration if available
        if (builder.Configuration["Logging:MinimumLevel"] != null)
            builder.Logging.SetMinimumLevel(
                Enum.Parse<LogLevel>(builder.Configuration["Logging:MinimumLevel"] ?? string.Empty));

        // Database Services
        builder.Services.AddSingleton<MongoDbService>();
        builder.Services.AddScoped<UserRepository>();
        builder.Services.AddScoped<ImageRepository>();

        // Api Services
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<GameRecordApiService>();
        builder.Services.AddSingleton<GenshinCharacterApiService>();
        builder.Services.AddSingleton<GenshinCharacterCardService>();
        builder.Services.AddSingleton<GenshinImageUpdaterService>();

        // LToken Services
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<CookieService>();
        builder.Services.AddSingleton<TokenCacheService>();

        // Other Services
        builder.Services.AddSingleton<PaginationCacheService>();

        // NetCord Services
        builder.Services.AddDiscordGateway().AddApplicationCommands()
            .AddComponentInteractions<ModalInteraction, ModalInteractionContext>()
            .AddComponentInteractions<StringMenuInteraction, StringMenuInteractionContext>()
            .AddComponentInteractions<ButtonInteraction, ButtonInteractionContext>();

        var host = builder.Build();

        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("G-Buddy application starting");

        host.AddModules(typeof(Program).Assembly);

        host.UseGatewayEventHandlers();
        logger.LogInformation("Discord gateway initialized");

        await host.RunAsync();
    }
}
