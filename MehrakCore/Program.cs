#region

using System.Globalization;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Models;
using MehrakCore.Modules;
using MehrakCore.Repositories;
using MehrakCore.Services;
using MehrakCore.Services.Commands;
using MehrakCore.Services.Commands.Common;
using MehrakCore.Services.Commands.Executor;
using MehrakCore.Services.Commands.Genshin;
using MehrakCore.Services.Commands.Genshin.Character;
using MehrakCore.Services.Commands.Hsr;
using MehrakCore.Services.Commands.Hsr.Character;
using MehrakCore.Services.Commands.Hsr.RealTimeNotes;
using MehrakCore.Services.Common;
using MehrakCore.Services.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using NetCord;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Hosting.Services.ComponentInteractions;
using NetCord.Services.ComponentInteractions;
using Serilog;
using StackExchange.Redis;

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

        // Configure Serilog
        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console(
                outputTemplate:
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                formatProvider: CultureInfo.InvariantCulture
            )
            .WriteTo.File(
                "logs/log-.txt",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31,
                outputTemplate:
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                formatProvider: CultureInfo.InvariantCulture
            );

        if (builder.Environment.IsDevelopment())
        {
            Console.WriteLine("Development environment detected");
            builder.Configuration.AddJsonFile("appsettings.development.json");
            loggerConfig.MinimumLevel.Debug();
        }

        Log.Logger = loggerConfig.CreateLogger();

        try
        {
            Log.Information("Starting MehrakBot application");

            // Configure logging to use Serilog
            builder.Logging.ClearProviders();
            builder.Logging.AddSerilog(dispose: true);

            // Database Services
            builder.Services.AddSingleton<MongoDbService>();
            builder.Services.AddScoped<UserRepository>();
            builder.Services.AddSingleton<ImageRepository>();
            BsonSerializer.RegisterSerializer(new EnumSerializer<GameName>(BsonType.String));

            // Api Services
            builder.Services.AddHttpClient("Default").ConfigurePrimaryHttpMessageHandler(() =>
                new HttpClientHandler
                {
                    UseCookies = false
                });
            builder.Services.AddSingleton<GameRecordApiService>();
            builder.Services.AddTransient<IDailyCheckInService, DailyCheckInService>();

            // Genshin Services
            builder.Services
                .AddSingleton<ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail>,
                    GenshinCharacterApiService>();
            builder.Services
                .AddSingleton<ICharacterCardService<GenshinCharacterInformation>, GenshinCharacterCardService>();
            builder.Services.AddSingleton<GenshinImageUpdaterService>();
            builder.Services
                .AddTransient<ICharacterCommandExecutor<GenshinCommandModule>, GenshinCharacterCommandExecutor>();

            // Hsr Services
            builder.Services
                .AddSingleton<ICharacterApi<HsrBasicCharacterData, HsrCharacterInformation>,
                    HsrCharacterApiService>();
            builder.Services
                .AddSingleton<ICharacterCardService<HsrCharacterInformation>, HsrCharacterCardService>();
            builder.Services.AddSingleton<ImageUpdaterService<HsrCharacterInformation>, HsrImageUpdaterService>();
            builder.Services
                .AddTransient<ICharacterCommandExecutor<HsrCommandModule>, HsrCharacterCommandExecutor>();
            builder.Services.AddSingleton<HsrCharacterAutocompleteService>();
            builder.Services.AddSingleton<IRealTimeNotesApiService<HsrRealTimeNotesData>, HsrRealTimeNotesApiService>();
            builder.Services
                .AddTransient<IRealTimeNotesCommandExecutor<HsrCommandModule>, HsrRealTimeNotesCommandExecutor>();

            // Daily Check-In Services
            builder.Services
                .AddTransient<IDailyCheckInCommandExecutor, DailyCheckInCommandExecutor>();

            // LToken Services
            IConnectionMultiplexer multiplexer = await ConnectionMultiplexer.ConnectAsync(
                builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379");
            builder.Services.AddSingleton(multiplexer);
            builder.Services.AddStackExchangeRedisCache(options =>
            {
                options.ConnectionMultiplexerFactory = () => Task.FromResult(multiplexer);
                options.InstanceName = "MehrakBot_";
            });
            builder.Services.AddSingleton<CookieService>();
            builder.Services.AddSingleton<TokenCacheService>();
            builder.Services.AddSingleton<IAuthenticationMiddlewareService, AuthenticationMiddlewareService>();

            // Other Services
            builder.Services.AddMemoryCache();
            builder.Services.AddSingleton<CommandRateLimitService>();
            builder.Services.AddTransient<PrometheusClientService>();

            // NetCord Services
            builder.Services.AddDiscordGateway().AddApplicationCommands()
                .AddComponentInteractions<ModalInteraction, ModalInteractionContext>()
                .AddComponentInteractions<ButtonInteraction, ButtonInteractionContext>();

            // Metrics
            builder.Services.AddHostedService<MetricsService>();

            var host = builder.Build();

            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("MehrakBot application starting");

            // Configure MongoDB
            var imageRepo = host.Services.GetRequiredService<ImageRepository>();

            foreach (var image in Directory.EnumerateFiles($"{AppContext.BaseDirectory}Assets", "*",
                         SearchOption.AllDirectories))
            {
                var fileName = Path.GetFileName(image).Split('.')[0];
                if (await imageRepo.FileExistsAsync(fileName)) continue;

                await using var stream = File.OpenRead(image);
                await imageRepo.UploadFileAsync(fileName, stream);
                logger.LogInformation("Uploaded {FileName} to MongoDB, file path {Image}", fileName, image);
            }

            host.AddModules(typeof(Program).Assembly);

            host.UseGatewayEventHandlers();
            logger.LogInformation("Discord gateway initialized");

            await host.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}
