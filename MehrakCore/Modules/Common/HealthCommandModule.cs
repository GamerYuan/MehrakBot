#region

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
        InteractionMessageProperties response = new();
        var container = new ComponentContainerProperties();
        response.WithFlags(MessageFlags.IsComponentsV2);
        response.AddComponents([container]);

        var systemUsage = await m_PrometheusClientService.GetSystemResourceAsync();

        var mongoDbStatus = await m_MongoDbService.IsConnected();
        var cacheStatus = m_RedisConnection.IsConnected;
    }

    private static string FormatApiStatus(IEnumerable<(string, bool)> apiStatus)
    {
        return string.Join('\n', apiStatus.Select(x => $"{x.Item1}: {(x.Item2 ? "Online" : "Offline")}"));
    }
}
