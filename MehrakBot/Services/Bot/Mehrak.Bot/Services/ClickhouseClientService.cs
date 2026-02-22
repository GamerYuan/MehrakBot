using System.Data.Common;
using ClickHouse.Driver.ADO;
using Mehrak.Domain.Models;
using Mehrak.Infrastructure.Config;
using Microsoft.Extensions.Options;

namespace Mehrak.Bot.Services;

public class ClickhouseClientService
{
    private readonly ClickhouseConfig m_Config;

    public ClickhouseClientService(IOptions<ClickhouseConfig> config)
    {
        m_Config = config.Value;
    }

    public async ValueTask<long> GetUniqueUserCountAsync(CancellationToken token = default)
    {
        try
        {
            using DbConnection connection =
                new ClickHouseConnection($"Host={m_Config.Host};Protocol=http;Username={m_Config.Username};Password={m_Config.Password}");
            await connection.OpenAsync(token);

            var query = @"
                SELECT argMax(Value, TimeUnix) as val
                FROM otel_metrics_sum
                WHERE MetricName = 'bot_num_users'
                  AND TimeUnix >= (now() - INTERVAL 5 MINUTE)";

            var result = await ExecuteScalarAsync<long>(connection, query, token);
            return result >= 0 ? result : -1;
        }
        catch
        {
            return -1;
        }
    }

    public async ValueTask<SystemResource> GetSystemResourceAsync(CancellationToken token = default)
    {
        try
        {
            using DbConnection connection =
                new ClickHouseConnection($"Host={m_Config.Host};Protocol=http;Username={m_Config.Username};Password={m_Config.Password}");
            await connection.OpenAsync(token);

            // Calculate CPU usage: 100 * (1 - avg(idle_rate))
            // idle_rate = (max(value) - min(value)) / duration
            var cpuQuery = @"
                SELECT
                    100 * (1 - avg(idle_rate)) as val
                FROM
                (
                    SELECT
                        if(toUnixTimestamp(max(TimeUnix)) - toUnixTimestamp(min(TimeUnix)) > 0,
                           (max(Value) - min(Value)) / (toUnixTimestamp(max(TimeUnix)) - toUnixTimestamp(min(TimeUnix))),
                           0) as idle_rate
                    FROM otel_metrics_sum
                    WHERE MetricName = 'system.cpu.time'
                      AND Attributes['state'] = 'idle'
                      AND TimeUnix >= (now() - INTERVAL 1 MINUTE)
                    GROUP BY Attributes['cpu']
                    HAVING idle_rate >= 0
                )";

            // Get total memory: system.memory.limit
            var memTotalQuery = @"
                SELECT argMax(Value, TimeUnix) as val
                FROM otel_metrics_sum
                WHERE MetricName = 'system.memory.limit'
                  AND TimeUnix >= (now() - INTERVAL 5 MINUTE)";

            // Get available memory: sum of free, cached, buffered
            var memAvailableQuery = @"
                SELECT sum(val) as val
                FROM
                (
                    SELECT argMax(Value, TimeUnix) as val
                    FROM otel_metrics_sum
                    WHERE MetricName = 'system.memory.usage'
                      AND Attributes['state'] IN ('free', 'cached', 'buffered')
                      AND TimeUnix >= (now() - INTERVAL 5 MINUTE)
                    GROUP BY Attributes['state']
                )";

            var cpuUsage = await ExecuteScalarAsync<double>(connection, cpuQuery, token);
            var memoryTotal = await ExecuteScalarAsync<long>(connection, memTotalQuery, token);
            var memoryAvailable = await ExecuteScalarAsync<long>(connection, memAvailableQuery, token);

            return new SystemResource
            {
                CpuUsage = cpuUsage >= 0 ? cpuUsage : -1,
                MemoryUsed = (memoryTotal > 0 && memoryAvailable >= 0) ? (memoryTotal - memoryAvailable) : -1,
                MemoryTotal = memoryTotal > 0 ? memoryTotal : -1
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

    private static async Task<T> ExecuteScalarAsync<T>(DbConnection connection, string query, CancellationToken token = default) where T : struct
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = query;
            var result = await command.ExecuteScalarAsync(token);

            if (result == null || result == DBNull.Value)
                return (T)Convert.ChangeType(-1, typeof(T));

            return (T)Convert.ChangeType(result, typeof(T));
        }
        catch
        {
            return (T)Convert.ChangeType(-1, typeof(T));
        }
    }

}



