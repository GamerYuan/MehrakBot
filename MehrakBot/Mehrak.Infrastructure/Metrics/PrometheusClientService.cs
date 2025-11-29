#region

using System.Text.Json;
using Mehrak.Infrastructure.Models;

#endregion

namespace Mehrak.Infrastructure.Metrics;

public interface ISystemResourceClientService
{
    ValueTask<SystemResource> GetSystemResourceAsync();
}

public class PrometheusClientService : ISystemResourceClientService
{
    private readonly IHttpClientFactory m_HttpClientFactory;

    private const string PrometheusBaseUrl = "http://prometheus:9090/api/v1/";

    public PrometheusClientService(IHttpClientFactory httpClientFactory)
    {
        m_HttpClientFactory = httpClientFactory;
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
