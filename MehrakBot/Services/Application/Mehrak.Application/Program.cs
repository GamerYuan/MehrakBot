using Mehrak.Application;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Models;
using Mehrak.Application.Shared.Services;
using Mehrak.GameApi;
using Mehrak.Infrastructure;
using Mehrak.Infrastructure.Shared.Config;
using Mehrak.ServiceDefaults;
using Proto = Mehrak.Domain.Protobuf;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.AddServiceDefaults();
        builder.Services.AddOpenTelemetry()
            .WithMetrics(m => m.AddMeter("MehrakApplication"))
            .WithTracing(t => t.AddSource(ApplicationTelemetry.ActivitySourceName));

        if (builder.Environment.IsDevelopment())
        {
            Console.WriteLine("Development environment detected");
        }

        builder.AddSerilogOtlp("MehrakApplication");

        builder.Services.Configure<S3StorageConfig>(builder.Configuration.GetSection("Storage"));
        builder.Services.Configure<RedisConfig>(options =>
        {
            options.ConnectionString = builder.Configuration.GetConnectionString("redis") ?? options.ConnectionString;
            options.InstanceName = builder.Configuration.GetValue<string>("Redis:InstanceName") ?? "Mehrak_";
        });
        builder.Services.Configure<PgConfig>(options =>
            options.ConnectionString = builder.Configuration.GetConnectionString("mehrakdb") ?? options.ConnectionString);
        builder.Services.Configure<CommandDispatcherConfig>(builder.Configuration.GetSection("CommandDispatcher"));

        builder.Services.AddHttpClient("Default").ConfigurePrimaryHttpMessageHandler(() =>
            new HttpClientHandler
            {
                UseCookies = false
            }).ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30));

        builder.Services.AddHostedService<AssetInitializationService>();

        // Add services to the container.
        builder.Services.AddGrpc();
        builder.Services.AddGameApiServices();
        builder.Services.AddInfrastructureServices();
        builder.Services.AddApplicationServices();

        builder.Services.AddGrpcClient<Proto.ImageProcessorService.ImageProcessorServiceClient>(options =>
        {
            var address = builder.Configuration.GetConnectionString("image-processor") ?? "http://image-processor";
            options.Address = new Uri(address);
        });

        builder.Services.AddSingleton<CommandDispatcher>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<CommandDispatcher>());

        builder.Services.AddSingleton<IApplicationMetrics, ApplicationMetricsService>();

        var app = builder.Build();

        app.MapDefaultEndpoints();

        // Configure the HTTP request pipeline.
        app.MapGrpcService<GrpcApplicationService>();
        app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client.");

        await app.RunAsync();
    }
}
