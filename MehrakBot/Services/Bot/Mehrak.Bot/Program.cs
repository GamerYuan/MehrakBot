using Mehrak.Bot.Shared.Modules;
using Mehrak.Bot.Shared.Services;
using Mehrak.Bot.Shared.Services.RateLimit;
using Mehrak.Domain.Protobuf;
using Mehrak.Infrastructure;
using Mehrak.Infrastructure.Shared.Config;
using Mehrak.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Hosting.Services.ComponentInteractions;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;
using OpenTelemetry.Metrics;
using Serilog;

namespace Mehrak.Bot;

public class Program
{
    public static async Task Main(string[] args)
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

        builder.AddServiceDefaults();
        builder.Services.AddOpenTelemetry().WithMetrics(m => m.AddMeter("MehrakBot"));

        if (builder.Environment.IsDevelopment())
        {
            Console.WriteLine("Development environment detected");
        }

        builder.AddSerilogOtlp("MehrakBot");

        try
        {
            Log.Information("Starting MehrakBot application");

            // Database Services
            builder.Services.Configure<S3StorageConfig>(builder.Configuration.GetSection("Storage"));
            builder.Services.Configure<RedisConfig>(options =>
            {
                options.ConnectionString = builder.Configuration.GetConnectionString("redis") ?? options.ConnectionString;
                options.InstanceName = builder.Configuration.GetValue<string>("Redis:InstanceName") ?? "Mehrak_";
            });
            builder.Services.Configure<PgConfig>(options =>
                options.ConnectionString = builder.Configuration.GetConnectionString("mehrakdb") ?? options.ConnectionString);
            builder.Services.Configure<ClickhouseConfig>(builder.Configuration.GetSection("Clickhouse"));
            builder.Services.Configure<RateLimiterConfig>(builder.Configuration.GetSection("RateLimit"));

            builder.Services.AddInfrastructureServices();
            builder.Services.AddBotServices();
            builder.Services.AddGrpcClient<ApplicationService.ApplicationServiceClient>(options =>
            {
                var address = builder.Configuration.GetConnectionString("application") ??
                    throw new InvalidOperationException("Application service connection string is not configured.");
                options.Address = new Uri(address);
            });

            builder.Services.AddDiscordGateway().AddApplicationCommands(a => a.ResultHandler =
                new CustomCommandResultHandler<ApplicationCommandContext>())
                .AddComponentInteractions<ModalInteraction, ModalInteractionContext>()
                .AddComponentInteractions<ButtonInteraction, ButtonInteractionContext>();

            var host = builder.Build();

            var logger = host.Services.GetRequiredService<ILogger<Program>>();

            host.AddModules(typeof(Program).Assembly);

            logger.LogInformation("Discord gateway initialized");

            await host.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application start-up failed");
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}
