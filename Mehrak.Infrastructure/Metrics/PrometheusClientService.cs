#region

using Mehrak.Domain.Models;
using Mehrak.Infrastructure.Models;
using System.Text.Json;

#endregion

namespace Mehrak.Infrastructure.Metrics;

public class PrometheusClientService
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
            PrometheusResponse? cpuUsageQuery =
                await QueryPrometheusAsync(
                    "100 - (avg by (instance) (irate(node_cpu_seconds_total{mode=\"idle\"}[5m])) * 100)");
            PrometheusResponse? memoryAvailableQuery =
                await QueryPrometheusAsync("node_memory_MemAvailable_bytes");
            PrometheusResponse? memoryTotalQuery =
                await QueryPrometheusAsync("node_memory_MemTotal_bytes");

            double cpuUsage = cpuUsageQuery?.Data.Result.Count > 0
                ? double.Parse(cpuUsageQuery.Data.Result[0].Value[1].ToString()!)
                : -1;
            long memoryAvailable = memoryAvailableQuery?.Data.Result.Count > 0
                ? long.Parse(memoryAvailableQuery.Data.Result[0].Value[1].ToString()!)
                : -1;
            long memoryTotal = memoryTotalQuery?.Data.Result.Count > 0
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
        string encodedQuery = Uri.EscapeDataString(query);
        string url = $"{PrometheusBaseUrl}/query?query={encodedQuery}";

        HttpClient httpClient = m_HttpClientFactory.CreateClient();
        string response = await httpClient.GetStringAsync(url);
        return JsonSerializer.Deserialize<PrometheusResponse>(response);
    }
}
