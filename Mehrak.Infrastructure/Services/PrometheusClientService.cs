#region

using System.Text.Json;
using MehrakCore.Models;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Infrastructure.Services;

public class PrometheusClientService
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<PrometheusClientService> m_Logger;

    private const string PrometheusBaseUrl = "http://prometheus:9090/api/v1/";

    public PrometheusClientService(IHttpClientFactory httpClientFactory, ILogger<PrometheusClientService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async ValueTask<SystemResource> GetSystemResourceAsync()
    {
        try
        {
            var cpuUsageQuery =
                await QueryPrometheusAsync(
                    "100 - (avg by (instance) (irate(node_cpu_seconds_total{mode=\"idle\"}[5m])) * 100)");
            var memoryAvailableQuery =
                await QueryPrometheusAsync("node_memory_MemAvailable_bytes");
            var memoryTotalQuery =
                await QueryPrometheusAsync("node_memory_MemTotal_bytes");

            var cpuUsage = cpuUsageQuery?.Data.Result.Count > 0
                ? double.Parse(cpuUsageQuery.Data.Result[0].Value[1].ToString()!)
                : -1;
            var memoryAvailable = memoryAvailableQuery?.Data.Result.Count > 0
                ? long.Parse(memoryAvailableQuery.Data.Result[0].Value[1].ToString()!)
                : -1;
            var memoryTotal = memoryTotalQuery?.Data.Result.Count > 0
                ? long.Parse(memoryTotalQuery.Data.Result[0].Value[1].ToString()!)
                : -1;

            return new SystemResource
            {
                CpuUsage = cpuUsage,
                MemoryUsed = memoryTotal - memoryAvailable,
                MemoryTotal = memoryTotal
            };
        }
        catch
        {
            return new SystemResource
            {
                CpuUsage = -1,
                MemoryUsed = -1,
                MemoryTotal = -1
            };
        }
    }

    private async Task<PrometheusResponse?> QueryPrometheusAsync(string query)
    {
        var encodedQuery = Uri.EscapeDataString(query);
        var url = $"{PrometheusBaseUrl}/query?query={encodedQuery}";

        var httpClient = m_HttpClientFactory.CreateClient();
        var response = await httpClient.GetStringAsync(url);
        return JsonSerializer.Deserialize<PrometheusResponse>(response);
    }
}
