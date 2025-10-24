﻿using Mehrak.Application;
using Mehrak.Bot.Services;
using Mehrak.Domain.Repositories;
using Mehrak.GameApi;
using Mehrak.Infrastructure;
using Mehrak.Infrastructure.Config;
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
using Serilog;
using Serilog.Sinks.Grafana.Loki;
using StackExchange.Redis;
using System.Globalization;

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
            builder.Services.Configure<CharacterCacheConfig>(builder.Configuration.GetSection("CharacterCache"));

            // Api Services
            builder.Services.AddHttpClient("Default").ConfigurePrimaryHttpMessageHandler(() =>
                new HttpClientHandler
                {
                    UseCookies = false
                });

            builder.Services.AddInfrastructureServices();

            builder.Services.AddGameApiServices();

            builder.Services.AddApplicationServices();

            IConnectionMultiplexer multiplexer = await ConnectionMultiplexer.ConnectAsync(
                builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379");
            builder.Services.AddSingleton(multiplexer);
            builder.Services.AddStackExchangeRedisCache(options =>
            {
                options.ConnectionMultiplexerFactory = () => Task.FromResult(multiplexer);
                options.InstanceName = "MehrakBot_";
            });

            builder.Services.AddBotServices();

            builder.Services.AddDiscordGateway().AddApplicationCommands()
                .AddComponentInteractions<ModalInteraction, ModalInteractionContext>()
                .AddComponentInteractions<ButtonInteraction, ButtonInteractionContext>();

            var host = builder.Build();

            ILogger<Program> logger = host.Services.GetRequiredService<ILogger<Program>>();

            host.AddModules(typeof(Program).Assembly);

            host.UseGatewayHandlers();
            logger.LogInformation("Discord gateway initialized");

            IImageRepository imageRepo = host.Services.GetRequiredService<IImageRepository>();

            foreach (string image in Directory.EnumerateFiles($"{AppContext.BaseDirectory}Assets", "*.png",
                         SearchOption.AllDirectories))
            {
                if (Path.GetDirectoryName(image)?.Contains("Test") ?? false) continue;
                string fileName = Path.GetFileName(image).Split('.')[0];
                if (await imageRepo.FileExistsAsync(fileName)) continue;

                await using FileStream stream = File.OpenRead(image);
                await imageRepo.UploadFileAsync(fileName, stream);
                logger.LogInformation("Uploaded {FileName} to Image Repository, file path {Image}", fileName, image);
            }

            await host.StartAsync();
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
