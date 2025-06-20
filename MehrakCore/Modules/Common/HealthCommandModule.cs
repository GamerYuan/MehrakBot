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
        { "HoYoLAB Posts API", "bbs-api-os.hoyolab.com" },
        { "HoYoLAB Account API", "api-account-os.hoyolab.com" },
        { "HoYoWiki API", "sg-wiki-api-static.hoyolab.com" }
    };

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

        var apiStatus = string.Join('\n', HealthCheckComponents.ToAsyncEnumerable().SelectAwait(async x =>
        {
            Ping ping = new();
            var pingResult = await ping.SendPingAsync(x.Value);
            return $"**{x.Key}**: {(pingResult.Status == IPStatus.Success ? "Online" : "Offline")}";
        }).ToBlockingEnumerable());

        var systemUsage = await systemUsageTask;

        var convert = Math.Pow(1024, 3);
        container.AddComponents(new TextDisplayProperties("## Health Report"));
        container.AddComponents(new ComponentSeparatorProperties());
        if (systemUsage.CpuUsage < 0)
            container.AddComponents(new TextDisplayProperties(
                $"### __System Resources__\n" +
                $"System monitor is offline"));
        else
            container.AddComponents(new TextDisplayProperties(
                $"### __System Resources__\n" +
                $"CPU: {systemUsage.CpuUsage:N2}%\n" +
                $"Memory: {systemUsage.MemoryUsed / convert:N2}/{systemUsage.MemoryTotal / convert:N2} GB " +
                $"{(double)systemUsage.MemoryUsed / systemUsage.MemoryTotal * 100:N2}%"));

        container.AddComponents(new ComponentSeparatorProperties());
        container.AddComponents(new TextDisplayProperties($"### __System Status__\n" +
                                                          $"**MongoDB**: {(mongoDbStatus ? "Online" : "Offline")}\n" +
                                                          $"**Redis**: {(cacheStatus ? "Online" : "Offline")}\n" +
                                                          $"**Prometheus**: {(systemUsage.CpuUsage > 0 ? "Online" : "Offline")}\n" +
                                                          $"**Discord Latency**: {m_GatewayClient.Latency.TotalMilliseconds} ms"));

        container.AddComponents(new ComponentSeparatorProperties());
        container.AddComponents(new TextDisplayProperties($"### __API Status__\n" +
                                                          $"{apiStatus}"));

        await Context.Interaction.SendFollowupMessageAsync(response);
    }
}
