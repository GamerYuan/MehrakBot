#region

using MehrakCore.Constants;
using MehrakCore.Services.Common;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using StackExchange.Redis;
using System.Net.NetworkInformation;

#endregion

namespace MehrakCore.Modules.Common;

public class HealthCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly MongoDbService m_MongoDbService;
    private readonly IConnectionMultiplexer m_RedisConnection;
    private readonly GatewayClient m_GatewayClient;
    private readonly PrometheusClientService m_PrometheusClientService;

    private static readonly Dictionary<string, string> HealthCheckComponents = new()
    {
        { "HoYoLAB API", new Uri(HoYoLabDomains.PublicApi).Host },
        { "HoYoLAB Genshin API", new Uri(HoYoLabDomains.GenshinApi).Host },
        { "HoYoLAB Genshin Operations API", new Uri(HoYoLabDomains.GenshinOpsApi).Host },
        { "HoYoLAB HSR Operations API", new Uri(HoYoLabDomains.HsrOpsApi).Host },
        { "HoYoLAB ZZZ Operations API", new Uri(HoYoLabDomains.ZzzOpsApi).Host },
        { "HoYoLAB Posts API", new Uri(HoYoLabDomains.PostsApi).Host },
        { "HoYoLAB Account API", new Uri(HoYoLabDomains.AccountApi).Host },
        { "HoYoWiki API", new Uri(HoYoLabDomains.WikiApi).Host }
    };

    private static int _maxCharCount;

    private static int MaxCharCount
    {
        get
        {
            if (_maxCharCount == 0) _maxCharCount = HealthCheckComponents.Keys.Select(x => x.Length).Max() + 11;
            return _maxCharCount;
        }
    }

    public HealthCommandModule(MongoDbService mongoDbService, IConnectionMultiplexer redisConnection,
        GatewayClient gatewayClient, PrometheusClientService prometheusClientService)
    {
        m_MongoDbService = mongoDbService;
        m_RedisConnection = redisConnection;
        m_GatewayClient = gatewayClient;
        m_PrometheusClientService = prometheusClientService;
    }

    [SlashCommand("health", "Check the health of the bot and its services.",
        DefaultGuildUserPermissions = Permissions.ManageGuild | Permissions.Administrator,
        Contexts = [InteractionContextType.Guild])]
    public async Task CheckHealthAsync()
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());

        InteractionMessageProperties response = new();
        var container = new ComponentContainerProperties();
        response.WithFlags(MessageFlags.IsComponentsV2);
        response.AddComponents([container]);

        var systemUsageTask = m_PrometheusClientService.GetSystemResourceAsync();

        var mongoDbStatus = await m_MongoDbService.IsConnected();
        var cacheStatus = m_RedisConnection.IsConnected;

        var pingTasks = HealthCheckComponents.Select(async x =>
        {
            Ping ping = new();
            var pingResult = await ping.SendPingAsync(x.Value);
            return GetFormattedStatus(x.Key, pingResult.Status == IPStatus.Success ? "Online" : "Offline",
                pingResult.Status == IPStatus.Success);
        }).ToList();

        var apiStatus = string.Join('\n', await Task.WhenAll(pingTasks));
        var systemUsage = await systemUsageTask;

        var convert = Math.Pow(1024, 3);
        container.AddComponents(new TextDisplayProperties("## Health Report"));
        container.AddComponents(new ComponentSeparatorProperties());
        if (systemUsage.CpuUsage < 0)
        {
            container.AddComponents(new TextDisplayProperties(
                $"### __System Resources__\n" +
                $"System monitor is offline"));
        }
        else
        {
            var memoryPercentage = (double)systemUsage.MemoryUsed / systemUsage.MemoryTotal;
            container.AddComponents(new TextDisplayProperties(
                $"### __System Resources__\n" +
                "```ansi\n" +
                GetFormattedStatus("CPU", $"{systemUsage.CpuUsage:N2}%", systemUsage.CpuUsage <= 80) +
                GetFormattedStatus("Memory",
                    $"{systemUsage.MemoryUsed / convert:N2}/{systemUsage.MemoryTotal / convert:N2} GB " +
                    $"{memoryPercentage * 100:N2}%",
                    memoryPercentage <= 0.8) +
                "```"));
        }

        container.AddComponents(new ComponentSeparatorProperties());
        container.AddComponents(new TextDisplayProperties($"### __System Status__\n" +
                                                          "```ansi\n" +
                                                          GetFormattedStatus("MongoDB",
                                                              mongoDbStatus
                                                                  ? "Online"
                                                                  : "Offline", mongoDbStatus) + "\n" +
                                                          GetFormattedStatus("Redis",
                                                              cacheStatus
                                                                  ? "Online"
                                                                  : "Offline", cacheStatus) + "\n" +
                                                          GetFormattedStatus("Prometheus",
                                                              systemUsage.CpuUsage > 0
                                                                  ? "Online"
                                                                  : "Offline", systemUsage.CpuUsage > 0) +
                                                          "\n" +
                                                          GetFormattedStatus("Discord Latency",
                                                              $"{m_GatewayClient.Latency.TotalMilliseconds:N0} ms",
                                                              m_GatewayClient.Latency.TotalMilliseconds <= 200) + "\n" +
                                                          "```"));

        container.AddComponents(new ComponentSeparatorProperties());
        container.AddComponents(new TextDisplayProperties($"### __API Status__\n" +
                                                          $"```ansi\n{apiStatus}```"));

        await Context.Interaction.SendFollowupMessageAsync(response);
    }

    private static string GetFormattedStatus(string serviceName, string value, bool threshold)
    {
        return
            $"\e[1m{serviceName}\e[0m{new string(' ', MaxCharCount - serviceName.Length - value.Length)}" +
            $"{(threshold ? $"\e[0;32m{value}\e[0m" : $"\e[0;31m{value}\e[0m")}";
    }

    public static string GetHelpString()
    {
        return "## Health\n" +
               "Checks the health status of Mehrak and HoYoverse public APIs.\n" +
               "### Usage\n" +
               "```/health```\n" +
               "-# This command is only available to users with the `Manage Server` or `Administrator` permissions.\n";
    }
}
