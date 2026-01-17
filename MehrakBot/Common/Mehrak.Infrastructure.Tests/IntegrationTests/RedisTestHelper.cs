using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Options;
using Testcontainers.Redis;

namespace Mehrak.Infrastructure.Tests.IntegrationTests;

internal sealed class RedisTestHelper : IAsyncDisposable
{
    public static RedisTestHelper Instance { get; } = new();

    private RedisContainer? m_Container;
    private string m_ConnectionString = string.Empty;

    public async Task InitAsync()
    {
        if (m_Container != null)
            return;

        m_Container = new RedisBuilder()
            .WithImage("redis:7.2-alpine")
            .Build();

        await m_Container.StartAsync();
        var port = m_Container.GetMappedPublicPort(6379);
        m_ConnectionString = $"{m_Container.Hostname}:{port}";
    }

    public RedisCache CreateCache()
    {
        if (string.IsNullOrWhiteSpace(m_ConnectionString))
            throw new InvalidOperationException("Redis container has not been initialized.");

        var options = Options.Create(new RedisCacheOptions
        {
            Configuration = m_ConnectionString,
            InstanceName = "mehrak-tests:"
        });

        return new RedisCache(options);
    }

    public async ValueTask DisposeAsync()
    {
        if (m_Container != null)
        {
            await m_Container.StopAsync();
            await m_Container.DisposeAsync();
            m_Container = null;
        }

        m_ConnectionString = string.Empty;
    }
}
