using System.Collections.Concurrent;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using Mehrak.Domain.Character.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Infrastructure.Character;
using Mehrak.Infrastructure.Character.Models;
using Mehrak.Infrastructure.Character.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Mehrak.Infrastructure.Tests.Character.Services;

[TestFixture]
[NonParallelizable]
internal sealed class CharacterPortraitConfigPostgreSqlCacheTests
{
    private PostgreSqlContainer m_Container = null!;
    private DbContextOptions<CharacterDbContext> m_Options = null!;
    private readonly ConcurrentDictionary<string, byte[]> m_CacheData = new();
    private Mock<IDistributedCache> m_Cache = null!;
    private const string SingleKey = "portrait_cfg_Genshin_100_7";
    private const string AllKey = "portrait_cfg_all_Genshin";

    [OneTimeSetUp]
    public async Task StartDatabase()
    {
        m_Container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await m_Container.StartAsync();
        m_Options = new DbContextOptionsBuilder<CharacterDbContext>()
            .UseNpgsql(m_Container.GetConnectionString()).Options;
        await using var context = new CharacterDbContext(m_Options);
        await context.Database.EnsureCreatedAsync();
    }

    [OneTimeTearDown]
    public async Task StopDatabase() => await m_Container.DisposeAsync();

    [SetUp]
    public async Task SetUp()
    {
        await using var context = new CharacterDbContext(m_Options);
        await context.CharacterPortraitConfigs.ExecuteDeleteAsync();
        await context.CharacterServerIds.ExecuteDeleteAsync();
        await context.Characters.ExecuteDeleteAsync();
        var character = new CharacterModel { Game = Game.Genshin, Name = "Test" };
        context.Characters.Add(character);
        await context.SaveChangesAsync();
        context.CharacterServerIds.Add(new CharacterServerIdModel { CharacterId = character.Id, ServerId = 100 });
        context.CharacterPortraitConfigs.Add(new CharacterPortraitConfigModel
        {
            Game = Game.Genshin, ServerId = 100, SubId = 7, Name = "Test", OffsetX = 1
        });
        await context.SaveChangesAsync();

        m_CacheData.Clear();
        m_Cache = new Mock<IDistributedCache>();
        m_Cache.Setup(cache => cache.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string key, CancellationToken _) =>
                Task.FromResult(m_CacheData.TryGetValue(key, out var value) ? value : null));
        m_Cache.Setup(cache => cache.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .Returns((string key, byte[] value, DistributedCacheEntryOptions _, CancellationToken _) =>
            {
                m_CacheData[key] = value;
                return Task.CompletedTask;
            });
        m_Cache.Setup(cache => cache.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string key, CancellationToken _) =>
            {
                m_CacheData.TryRemove(key, out var removed);
                return Task.CompletedTask;
            });
    }

    private ServiceProvider CreateProvider(params IInterceptor[] interceptors)
    {
        var services = new ServiceCollection();
        var options = new DbContextOptionsBuilder<CharacterDbContext>(m_Options)
            .AddInterceptors(interceptors).Options;
        services.AddScoped(_ => new CharacterDbContext(options));
        return services.BuildServiceProvider();
    }

    private CharacterPortraitConfigService CreateService(ServiceProvider provider) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), m_Cache.Object,
            NullLogger<CharacterPortraitConfigService>.Instance);

    [Test]
    public async Task UpsertConfigAsync_SingleConnectionPool_PublishesWithoutPoolExhaustion()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(m_Container.GetConnectionString())
        {
            MaxPoolSize = 1,
            Timeout = 2
        }.ConnectionString;
        var options = new DbContextOptionsBuilder<CharacterDbContext>()
            .UseNpgsql(connectionString).Options;
        var services = new ServiceCollection();
        services.AddScoped(_ => new CharacterDbContext(options));
        await using var provider = services.BuildServiceProvider();
        var logger = new RecordingLogger();
        var service = new CharacterPortraitConfigService(
            provider.GetRequiredService<IServiceScopeFactory>(), m_Cache.Object, logger);
        await service.GetConfigAsync(Game.Genshin, 100, 7);
        await service.GetAllConfigsAsync(Game.Genshin);
        AssertSingleCache(1);
        Assert.That(m_CacheData.ContainsKey(AllKey), Is.True);

        Assert.That(await service.UpsertConfigAsync(Game.Genshin, 100,
            new CharacterPortraitConfigUpdate { OffsetX = 3 }, 7).WaitAsync(TimeSpan.FromSeconds(10)), Is.True);

        Assert.That(logger.Errors, Is.Empty, "Cache publication must not swallow connection pool exhaustion.");
        AssertSingleCache(3);
        Assert.That(m_CacheData.ContainsKey(AllKey), Is.False);
        await using var context = new CharacterDbContext(options);
        Assert.That((await context.CharacterPortraitConfigs.SingleAsync()).OffsetX, Is.EqualTo(3));
    }

    [Test]
    public async Task DelayedWriterPublication_ReloadsNewerCommit()
    {
        var gate = new CommitGate();
        await using var firstProvider = CreateProvider(gate);
        await using var secondProvider = CreateProvider();
        var first = CreateService(firstProvider).UpsertConfigAsync(Game.Genshin, 100,
            new CharacterPortraitConfigUpdate { OffsetX = 2 }, 7);
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.That(await CreateService(secondProvider).UpsertConfigAsync(Game.Genshin, 100,
                new CharacterPortraitConfigUpdate { OffsetX = 3 }, 7), Is.True);
        }
        finally
        {
            gate.Release.TrySetResult();
        }
        Assert.That(await first.WaitAsync(TimeSpan.FromSeconds(15)), Is.True);
        AssertSingleCache(3);
        Assert.That((await CreateService(secondProvider).GetAllConfigsAsync(Game.Genshin))["Test_100_7"].OffsetX,
            Is.EqualTo(3));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ReadFill_HoldsOrderingLockUntilPublication(bool aggregate)
    {
        await using var readerProvider = CreateProvider();
        await using var writerProvider = CreateProvider();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var keyToGate = aggregate ? AllKey : SingleKey;
        var gated = 0;
        m_Cache.Setup(cache => cache.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .Returns(async (string key, byte[] value, DistributedCacheEntryOptions _, CancellationToken _) =>
            {
                if (key == keyToGate && Interlocked.CompareExchange(ref gated, 1, 0) == 0)
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(TimeSpan.FromSeconds(20));
                }
                m_CacheData[key] = value;
            });

        var reader = CreateService(readerProvider);
        Task fill = aggregate ? reader.GetAllConfigsAsync(Game.Genshin) : reader.GetConfigAsync(Game.Genshin, 100, 7);
        Task<bool>? writer = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            writer = CreateService(writerProvider).UpsertConfigAsync(Game.Genshin, 100,
                new CharacterPortraitConfigUpdate { OffsetX = 3 }, 7);
            await WaitForBlockedWriterAsync();
            await using var context = new CharacterDbContext(m_Options);
            Assert.That((await context.CharacterPortraitConfigs.SingleAsync()).OffsetX, Is.EqualTo(1),
                "A fill must finish publishing before a writer can commit.");
        }
        finally
        {
            release.TrySetResult();
            await fill.WaitAsync(TimeSpan.FromSeconds(15));
            if (writer != null)
                Assert.That(await writer.WaitAsync(TimeSpan.FromSeconds(15)), Is.True);
        }

        AssertSingleCache(3);
        Assert.That(m_CacheData.ContainsKey(AllKey), Is.False, "Writer invalidates the earlier aggregate fill.");
        Assert.That((await reader.GetAllConfigsAsync(Game.Genshin))["Test_100_7"].OffsetX, Is.EqualTo(3));
    }

    // Observe the real PostgreSQL advisory-lock wait instead of guessing timing with sleeps.
    private async Task WaitForBlockedWriterAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var connection = new NpgsqlConnection(m_Container.GetConnectionString());
        await connection.OpenAsync(timeout.Token);
        while (true)
        {
            await using var command = new NpgsqlCommand(
                "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname = current_database() " +
                "AND wait_event_type = 'Lock' AND wait_event = 'advisory')", connection);
            if ((bool)(await command.ExecuteScalarAsync(timeout.Token))!)
                return;
            await Task.Yield();
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CacheFailureAfterCommit_PreservesSuccessfulWrite(bool failInvalidation)
    {
        await using var provider = CreateProvider();
        if (failInvalidation)
            m_Cache.Setup(cache => cache.RemoveAsync(AllKey, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Cache unavailable"));
        else
            m_Cache.Setup(cache => cache.SetAsync(SingleKey, It.IsAny<byte[]>(),
                    It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Cache unavailable"));

        Assert.That(await CreateService(provider).UpsertConfigAsync(Game.Genshin, 100,
            new CharacterPortraitConfigUpdate { OffsetX = 9 }, 7), Is.True);
        await using var context = new CharacterDbContext(m_Options);
        Assert.That((await context.CharacterPortraitConfigs.SingleAsync()).OffsetX, Is.EqualTo(9));
    }

    private void AssertSingleCache(int expected)
    {
        Assert.That(m_CacheData.ContainsKey(SingleKey), Is.True);
        using var json = JsonDocument.Parse(Encoding.UTF8.GetString(m_CacheData[SingleKey]));
        Assert.That(json.RootElement.GetProperty("OffsetX").GetInt32(), Is.EqualTo(expected));
        Assert.That(json.RootElement.GetProperty("SubId").GetInt32(), Is.EqualTo(7));
    }

    private sealed class RecordingLogger : ILogger<CharacterPortraitConfigService>
    {
        public ConcurrentQueue<string> Errors { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
                Errors.Enqueue($"{formatter(state, exception)} {exception}");
        }
    }

    private sealed class CommitGate : DbTransactionInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int m_Used;

        public override async Task TransactionCommittedAsync(DbTransaction transaction,
            TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref m_Used, 1, 0) != 0)
                return;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
    }
}
