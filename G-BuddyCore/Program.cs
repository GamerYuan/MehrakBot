using G_BuddyCore.Repositories;
using G_BuddyCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetCord;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Hosting.Services.ComponentInteractions;
using NetCord.Services.ComponentInteractions;

namespace G_BuddyCore;

internal class Program
{
    static async Task Main(string[] args)
    {
        HostApplicationBuilderSettings settings = new()
        {
            Args = args,
            Configuration = new ConfigurationManager(),
            ContentRootPath = Directory.GetCurrentDirectory(),
        };

        settings.Configuration.AddJsonFile("appsettings.json")
            .AddUserSecrets<Program>()
            .AddEnvironmentVariables()
            .Build();

        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddDiscordGateway().AddApplicationCommands()
            .AddComponentInteractions<ModalInteraction, ModalInteractionContext>();
        builder.Services.AddSingleton<MongoDbService>();
        builder.Services.AddScoped<UserRepository>();

        var host = builder.Build();

        host.AddModules(typeof(Program).Assembly);

        host.UseGatewayEventHandlers();

        await host.RunAsync();
    }
}
