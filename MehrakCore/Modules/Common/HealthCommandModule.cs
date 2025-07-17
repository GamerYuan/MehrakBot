#region

using System.Net.NetworkInformation;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Services;
using MehrakCore.Services.Commands;
using MehrakCore.Services.Common;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using StackExchange.Redis;

#endregion

namespace MehrakCore.Modules.Common;

public class HealthCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly MongoDbService m_MongoDbService;
    private readonly IConnectionMultiplexer m_RedisConnection;
    private readonly IDailyCheckInService m_DailyCheckInService;
    private readonly ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail> m_GenshinCharacterApi;
    private readonly ICharacterApi<HsrBasicCharacterData, HsrCharacterInformation> m_HsrCharacterApi;
    private readonly GatewayClient m_GatewayClient;
    private readonly PrometheusClientService m_PrometheusClientService;
    private readonly GameRecordApiService m_GameRecordApiService;

    private static readonly Dictionary<string, string> HealthCheckComponents = new()
    {
        { "HoYoLAB API", "sg-public-api.hoyolab.com" },
        { "HoYoLAB Genshin API", "sg-hk4e-api.hoyolab.com" },
        { "HoYoLAB Genshin Operations API", "public-operation-hk4e.hoyolab.com" },
        { "HoYoLAB HSR Operations API", "public-operation-hkrpg.hoyolab.com" },
        { "HoYoLAB ZZZ Operations API", "public-operation-nap.hoyolab.com" },
        { "HoYoLAB Posts API", "bbs-api-os.hoyolab.com" },
        { "HoYoLAB Account API", "api-account-os.hoyolab.com" },
        { "HoYoWiki API", "sg-wiki-api-static.hoyolab.com" }
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
        IDailyCheckInService dailyCheckInService, GatewayClient gatewayClient,
        PrometheusClientService prometheusClientService, GameRecordApiService gameRecordApiService,
        ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail> genshinCharacterApi,
        ICharacterApi<HsrBasicCharacterData, HsrCharacterInformation> hsrCharacterApi)
    {
        m_MongoDbService = mongoDbService;
        m_RedisConnection = redisConnection;
        m_DailyCheckInService = dailyCheckInService;
        m_GatewayClient = gatewayClient;
        m_PrometheusClientService = prometheusClientService;
        m_GameRecordApiService = gameRecordApiService;
        m_GenshinCharacterApi = genshinCharacterApi;
        m_HsrCharacterApi = hsrCharacterApi;
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
