#region

using System.Globalization;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Models;
using MehrakCore.Repositories;
using MehrakCore.Services;
using MehrakCore.Services.Genshin;
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
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;
using Serilog;

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
            builder.Services
                .AddSingleton<ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail>,
                    GenshinCharacterApiService>();
            builder.Services
                .AddSingleton<ICharacterCardService<GenshinCharacterInformation>, GenshinCharacterCardService>();
            builder.Services.AddSingleton<GenshinImageUpdaterService>();
            builder.Services.AddTransient<GenshinCharacterCommandService<ApplicationCommandContext>>();
            builder.Services.AddTransient<GenshinCharacterCommandService<ModalInteractionContext>>();
            builder.Services.AddTransient<IDailyCheckInService, GenshinDailyCheckInService>();

            // LToken Services
            builder.Services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = builder.Configuration["Redis:ConnectionString"];
                options.InstanceName = "MehrakBot_";
            });
            builder.Services.AddSingleton<CookieService>();
            builder.Services.AddSingleton<TokenCacheService>();

            // Other Services
            // Replace memory cache with Redis for rate limiting
            builder.Services.AddSingleton<CommandRateLimitService>();

            // NetCord Services
            builder.Services.AddDiscordGateway().AddApplicationCommands()
                .AddComponentInteractions<ModalInteraction, ModalInteractionContext>()
                .AddComponentInteractions<ButtonInteraction, ButtonInteractionContext>();

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
