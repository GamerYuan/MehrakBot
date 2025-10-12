#region

using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Infrastructure.Metrics;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.ApiResponseTypes.Zzz;
using MehrakCore.Models;
using MehrakCore.Modules;
using MehrakCore.Services;
using MehrakCore.Services.Commands;
using MehrakCore.Services.Commands.Common;
using MehrakCore.Services.Commands.Executor;
using MehrakCore.Services.Commands.Genshin;
using MehrakCore.Services.Commands.Genshin.Abyss;
using MehrakCore.Services.Commands.Genshin.Character;
using MehrakCore.Services.Commands.Genshin.CharList;
using MehrakCore.Services.Commands.Genshin.CodeRedeem;
using MehrakCore.Services.Commands.Genshin.RealTimeNotes;
using MehrakCore.Services.Commands.Genshin.Stygian;
using MehrakCore.Services.Commands.Genshin.Theater;
using MehrakCore.Services.Commands.Hsr;
using MehrakCore.Services.Commands.Hsr.Character;
using MehrakCore.Services.Commands.Hsr.CharList;
using MehrakCore.Services.Commands.Hsr.CodeRedeem;
using MehrakCore.Services.Commands.Hsr.EndGame;
using MehrakCore.Services.Commands.Hsr.EndGame.BossChallenge;
using MehrakCore.Services.Commands.Hsr.EndGame.PureFiction;
using MehrakCore.Services.Commands.Hsr.Memory;
using MehrakCore.Services.Commands.Hsr.RealTimeNotes;
using MehrakCore.Services.Commands.Zzz;
using MehrakCore.Services.Commands.Zzz.Assault;
using MehrakCore.Services.Commands.Zzz.Character;
using MehrakCore.Services.Commands.Zzz.CodeRedeem;
using MehrakCore.Services.Commands.Zzz.Defense;
using MehrakCore.Services.Commands.Zzz.RealTimeNotes;
using MehrakCore.Services.Common;
using MehrakCore.Services.Metrics;
using MehrakCore.Utility;
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
using Serilog.Sinks.Grafana.Loki;
using StackExchange.Redis;
using System.Globalization;

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

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        if (builder.Environment.IsDevelopment())
        {
            Console.WriteLine("Development environment detected");
            builder.Configuration.AddJsonFile("appsettings.development.json");
        }

        // Configure Serilog
        LoggerConfiguration loggerConfig = new LoggerConfiguration()
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
            )
            .WriteTo.GrafanaLoki(
                builder.Configuration["Loki:ConnectionString"] ?? "http://localhost:3100",
                [
                    new LokiLabel { Key = "app", Value = "MehrakBot" },
                    new LokiLabel { Key = "environment", Value = builder.Environment.EnvironmentName }
                ]);

        if (builder.Environment.IsDevelopment())
            loggerConfig.MinimumLevel.Debug();

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
            builder.Services.AddSingleton<ICharacterRepository, CharacterRepository>();
            builder.Services.AddSingleton<IAliasRepository, AliasRepository>();
            builder.Services.AddSingleton<ICodeRedeemRepository, CodeRedeemRepository>();
            builder.Services.AddSingleton<IRelicRepository, HsrRelicRepository>();
            BsonSerializer.RegisterSerializer(new EnumSerializer<Game>(BsonType.String));

            // Character Cache Services
            builder.Services.AddHostedService<CharacterInitializationService>();
            builder.Services.AddHostedService<AliasInitializationService>();
            builder.Services.AddSingleton<ICharacterCacheService, CharacterCacheService>();
            builder.Services.Configure<CharacterCacheConfig>(builder.Configuration.GetSection("CharacterCache"));
            builder.Services.AddHostedService<CharacterCacheBackgroundService>();

            // Api Services
            builder.Services.AddHttpClient("Default").ConfigurePrimaryHttpMessageHandler(() =>
                new HttpClientHandler
                {
                    UseCookies = false
                });
            builder.Services.AddSingleton<GameRecordApiService>();
            builder.Services.AddTransient<IDailyCheckInService, DailyCheckInService>();

            // Genshin Services
            AddGenshinServices(builder);

            // Hsr Services
            AddHsrServices(builder);

            // Zzz Services
            AddZzzServices(builder);

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
            builder.Services.AddSingleton<CookieEncryptionService>();
            builder.Services.AddSingleton<RedisCacheService>();
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

            // Initialize services
            builder.Services.AddHostedService<AsyncInitializationHostedService>();

            RegisterAsyncInitializableServices(builder);

            IHost host = builder.Build();

            ILogger<Program> logger = host.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("MehrakBot application starting");

            // Configure MongoDB
            ImageRepository imageRepo = host.Services.GetRequiredService<ImageRepository>();

            foreach (string image in Directory.EnumerateFiles($"{AppContext.BaseDirectory}Assets", "*.png",
                         SearchOption.AllDirectories))
            {
                if (image.Contains("Test")) continue;
                string fileName = Path.GetFileName(image).Split('.')[0];
                if (await imageRepo.FileExistsAsync(fileName)) continue;

                await using FileStream stream = File.OpenRead(image);
                await imageRepo.UploadFileAsync(fileName, stream);
                logger.LogInformation("Uploaded {FileName} to MongoDB, file path {Image}", fileName, image);
            }

            host.AddModules(typeof(Program).Assembly);

            host.UseGatewayHandlers();
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

    private static void AddGenshinServices(HostApplicationBuilder builder)
    {
        builder.Services
                .AddSingleton<ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail>,
                    GenshinCharacterApiService>();
        builder.Services
            .AddSingleton<ICharacterCardService<GenshinCharacterInformation>, GenshinCharacterCardService>();
        builder.Services.AddSingleton<GenshinImageUpdaterService>();
        builder.Services
            .AddTransient<ICharacterCommandExecutor<GenshinCommandModule>, GenshinCharacterCommandExecutor>();
        builder.Services
            .AddSingleton<ICharacterAutocompleteService<GenshinCommandModule>,
                GenshinCharacterAutocompleteService>();
        builder.Services
            .AddSingleton<IRealTimeNotesApiService<GenshinRealTimeNotesData>, GenshinRealTimeNotesApiService>();
        builder.Services
            .AddTransient<IRealTimeNotesCommandExecutor<GenshinCommandModule>,
                GenshinRealTimeNotesCommandExecutor>();
        builder.Services.AddSingleton<ICodeRedeemApiService<GenshinCommandModule>, GenshinCodeRedeemApiService>();
        builder.Services
            .AddTransient<ICodeRedeemExecutor<GenshinCommandModule>, GenshinCodeRedeemExecutor>();
        builder.Services.AddTransient<GenshinAbyssCommandExecutor>();
        builder.Services.AddSingleton<IApiService<GenshinAbyssCommandExecutor>, GenshinAbyssApiService>();
        builder.Services.AddSingleton<ICommandService<GenshinAbyssCommandExecutor>, GenshinAbyssCardService>();
        builder.Services.AddTransient<GenshinTheaterCommandExecutor>();
        builder.Services.AddSingleton<IApiService<GenshinTheaterCommandExecutor>, GenshinTheaterApiService>();
        builder.Services.AddSingleton<ICommandService<GenshinTheaterCommandExecutor>, GenshinTheaterCardService>();
        builder.Services.AddTransient<GenshinStygianCommandExecutor>();
        builder.Services.AddSingleton<IApiService<GenshinStygianCommandExecutor>, GenshinStygianApiService>();
        builder.Services.AddSingleton<ICommandService<GenshinStygianCommandExecutor>, GenshinStygianCardService>();
        builder.Services.AddTransient<GenshinCharListCommandExecutor>();
        builder.Services
            .AddSingleton<ICommandService<GenshinCharListCommandExecutor>, GenshinCharListCardService>();
    }

    private static void AddHsrServices(HostApplicationBuilder builder)
    {
        builder.Services
                .AddSingleton<ICharacterApi<HsrBasicCharacterData, HsrCharacterInformation>,
                    HsrCharacterApiService>();
        builder.Services
            .AddSingleton<ICharacterCardService<HsrCharacterInformation>, HsrCharacterCardService>();
        builder.Services.AddSingleton<ImageUpdaterService<HsrCharacterInformation>, HsrImageUpdaterService>();
        builder.Services
            .AddTransient<ICharacterCommandExecutor<HsrCommandModule>, HsrCharacterCommandExecutor>();
        builder.Services
            .AddSingleton<ICharacterAutocompleteService<HsrCommandModule>, HsrCharacterAutocompleteService>();
        builder.Services.AddSingleton<IRealTimeNotesApiService<HsrRealTimeNotesData>, HsrRealTimeNotesApiService>();
        builder.Services
            .AddTransient<IRealTimeNotesCommandExecutor<HsrCommandModule>, HsrRealTimeNotesCommandExecutor>();
        builder.Services.AddSingleton<ICodeRedeemApiService<HsrCommandModule>, HsrCodeRedeemApiService>();
        builder.Services
            .AddTransient<ICodeRedeemExecutor<HsrCommandModule>, HsrCodeRedeemExecutor>();
        builder.Services.AddSingleton<IApiService<HsrMemoryCommandExecutor>, HsrMemoryApiService>();
        builder.Services.AddSingleton<ICommandService<HsrMemoryCommandExecutor>, HsrMemoryCardService>();
        builder.Services.AddTransient<HsrMemoryCommandExecutor>();
        builder.Services.AddSingleton<IApiService<BaseHsrEndGameCommandExecutor>, HsrEndGameApiService>();
        builder.Services.AddSingleton<ICommandService<BaseHsrEndGameCommandExecutor>, HsrEndGameCardService>();
        builder.Services.AddTransient<HsrPureFictionCommandExecutor>();
        builder.Services.AddTransient<HsrBossChallengeCommandExecutor>();
        builder.Services.AddSingleton<ICommandService<HsrCharListCommandExecutor>, HsrCharListCardService>();
        builder.Services.AddTransient<HsrCharListCommandExecutor>();
    }

    private static void AddZzzServices(HostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<ImageUpdaterService<ZzzFullAvatarData>, ZzzImageUpdaterService>();
        builder.Services
            .AddTransient<ICodeRedeemExecutor<ZzzCommandModule>, ZzzCodeRedeemExecutor>();
        builder.Services.AddSingleton<ICodeRedeemApiService<ZzzCommandModule>, ZzzCodeRedeemApiService>();
        builder.Services.AddSingleton<ICharacterApi<ZzzBasicAvatarData, ZzzFullAvatarData>,
            ZzzCharacterApiService>();
        builder.Services.AddSingleton<ICharacterCardService<ZzzFullAvatarData>, ZzzCharacterCardService>();
        builder.Services.AddTransient<ICharacterCommandExecutor<ZzzCommandModule>,
            ZzzCharacterCommandExecutor>();
        builder.Services.AddSingleton<IApiService<ZzzDefenseCommandExecutor>, ZzzDefenseApiService>();
        builder.Services.AddSingleton<ICommandService<ZzzDefenseCommandExecutor>, ZzzDefenseCardService>();
        builder.Services.AddTransient<ZzzDefenseCommandExecutor>();
        builder.Services.AddSingleton<IApiService<ZzzAssaultCommandExecutor>, ZzzAssaultApiService>();
        builder.Services.AddSingleton<ICommandService<ZzzAssaultCommandExecutor>, ZzzAssaultCardService>();
        builder.Services.AddHostedService<ZzzAssaultApiService>();
        builder.Services.AddTransient<ZzzAssaultCommandExecutor>();
        builder.Services.AddSingleton<IRealTimeNotesApiService<ZzzRealTimeNotesData>, ZzzRealTimeNotesApiService>();
        builder.Services.AddTransient<IRealTimeNotesCommandExecutor<ZzzCommandModule>, ZzzRealTimeNotesCommandExecutor>();
    }

    private static void RegisterAsyncInitializableServices(HostApplicationBuilder builder)
    {
        builder.Services.RegisterAsyncInitializable<ICharacterCardService<GenshinCharacterInformation>>();
        builder.Services.RegisterAsyncInitializableFor<ICommandService<GenshinAbyssCommandExecutor>, GenshinAbyssCardService>();
        builder.Services.RegisterAsyncInitializableFor<ICommandService<GenshinTheaterCommandExecutor>, GenshinTheaterCardService>();
        builder.Services.RegisterAsyncInitializableFor<ICommandService<GenshinStygianCommandExecutor>, GenshinStygianCardService>();

        builder.Services.RegisterAsyncInitializable<ICharacterCardService<HsrCharacterInformation>>();
        builder.Services.RegisterAsyncInitializableFor<ICommandService<HsrMemoryCommandExecutor>, HsrMemoryCardService>();
        builder.Services.RegisterAsyncInitializableFor<ICommandService<BaseHsrEndGameCommandExecutor>, HsrEndGameCardService>();

        builder.Services.RegisterAsyncInitializable<ICharacterCardService<ZzzFullAvatarData>>();
        builder.Services.RegisterAsyncInitializableFor<ICommandService<ZzzDefenseCommandExecutor>, ZzzDefenseCardService>();
        builder.Services.RegisterAsyncInitializableFor<ICommandService<ZzzAssaultCommandExecutor>, ZzzAssaultCardService>();
    }
}
