#region

#endregion

#region

using System.Net.NetworkInformation;
using Amazon.S3;
using Mehrak.Bot.Config;
using Mehrak.Bot.Services;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using StackExchange.Redis;

#endregion

namespace Mehrak.Bot.Modules.Common;

public class HealthCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly IDbStatusService m_DbStatus;
    private readonly IConnectionMultiplexer m_RedisConnection;
    private readonly GatewayClient m_GatewayClient;
    private readonly ClickhouseClientService m_SystemMetricsClient;
    private readonly IAmazonS3 m_S3;
    private static readonly Dictionary<string, string> HealthCheckComponents = new()
    {
        { "HoYoLAB API", new Uri(HoYoLabDomains.PublicApi).Host },
        { "HoYoLAB Genshin API", new Uri(HoYoLabDomains.GenshinApi).Host },
        { "HoYoLAB Genshin Operations API", new Uri(HoYoLabDomains.GenshinOpsApi).Host },
        { "HoYoLAB HSR Operations API", new Uri(HoYoLabDomains.HsrOpsApi).Host },
        { "HoYoLAB ZZZ Operations API", new Uri(HoYoLabDomains.ZzzOpsApi).Host },
        { "HoYoLAB Posts API", new Uri(HoYoLabDomains.BbsApi).Host },
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

    public HealthCommandModule(IDbStatusService dbStatus, IConnectionMultiplexer redisConnection,
        GatewayClient gatewayClient, ClickhouseClientService clickhouseClientService, IAmazonS3 s3)
    {
        m_DbStatus = dbStatus;
        m_RedisConnection = redisConnection;
        m_GatewayClient = gatewayClient;
        m_SystemMetricsClient = clickhouseClientService;
        m_S3 = s3;
    }

    [SlashCommand("health", "Check the health of the bot and its services.",
        DefaultGuildPermissions = Permissions.ManageGuild | Permissions.Administrator,
        Contexts = [InteractionContextType.Guild])]
    public async Task CheckHealthAsync()
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        InteractionMessageProperties response = new();
        var container = new ComponentContainerProperties();
        response.WithFlags(MessageFlags.IsComponentsV2);
        response.AddComponents([container]);

        var systemUsageTask = m_SystemMetricsClient.GetSystemResourceAsync(cts.Token);

        var dbStatus = await m_DbStatus.GetDbStatus();

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
                "\n" +
                GetFormattedStatus("Memory",
                    $"{systemUsage.MemoryUsed / convert:N2}/{systemUsage.MemoryTotal / convert:N2} GB " +
                    $"{memoryPercentage * 100:N2}%",
                    memoryPercentage <= 0.8) +
                "```"));
        }

        var s3Status = await CheckS3ConnectionAsync(cts.Token);

        container.AddComponents(new ComponentSeparatorProperties());
        container.AddComponents(new TextDisplayProperties($"### __System Status__\n" +
                                                          "```ansi\n" +
                                                          GetFormattedStatus("PostgreSQL",
                                                              dbStatus
                                                                  ? "Online"
                                                                  : "Offline", dbStatus) + "\n" +
                                                          GetFormattedStatus("Redis",
                                                              cacheStatus
                                                                  ? "Online"
                                                                  : "Offline", cacheStatus) + "\n" +
                                                          GetFormattedStatus("ClickHouse",
                                                              systemUsage.CpuUsage > 0
                                                                  ? "Online"
                                                                  : "Offline", systemUsage.CpuUsage > 0) +
                                                          "\n" +
                                                          GetFormattedStatus("SeaweedFS",
                                                            s3Status
                                                                ? "Online"
                                                                : "Offline", s3Status) +
                                                          "\n" +
                                                          GetFormattedStatus("Discord Latency",
                                                              $"{m_GatewayClient.Latency.TotalMilliseconds:N0} ms",
                                                              m_GatewayClient.Latency.TotalMilliseconds <= 200) + "\n" +
                                                          "```"));

        container.AddComponents(new ComponentSeparatorProperties());
        container.AddComponents(new TextDisplayProperties($"### __API Status__\n" +
                                                          $"```ansi\n{apiStatus}```"));
        container.AddComponents(new ComponentSeparatorProperties());
        container.AddComponents(new TextDisplayProperties($"-# v{AppInfo.Version}"));

        await Context.Interaction.SendFollowupMessageAsync(response);
    }

    private async ValueTask<bool> CheckS3ConnectionAsync(CancellationToken token = default)
    {
        try
        {
            await m_S3.ListBucketsAsync(token);
            return true;
        }
        catch (Exception e) when (e is AmazonS3Exception or HttpRequestException or OperationCanceledException)
        {
            return false;
        }
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
