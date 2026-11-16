using Amazon.S3;
using Amazon.S3.Model;
using Mehrak.Infrastructure.Character;
using Mehrak.Infrastructure.Character.Models;
using Mehrak.Infrastructure.Character.Services;
using Mehrak.Infrastructure.Shared.Config;
using Mehrak.Infrastructure.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Testcontainers.PostgreSql;

namespace Mehrak.Infrastructure.Tests.Character.Services;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
internal sealed class UserPortraitDeletionProcessorTests : IDisposable
{
    private readonly TestDbContextFactory m_DbFactory = new();
    private readonly Mock<IAmazonS3> m_S3 = new(MockBehavior.Strict);

    public void Dispose() => m_DbFactory.Dispose();

    private ServiceProvider CreateServices(params IInterceptor[] interceptors) => CreateServices(
        new DbContextOptionsBuilder<CharacterDbContext>(m_DbFactory.CreateContextOptions<CharacterDbContext>())
            .AddInterceptors(interceptors).Options);

    private static ServiceProvider CreateServices(DbContextOptions<CharacterDbContext> options) =>
        new ServiceCollection().AddScoped(_ => new CharacterDbContext(options)).BuildServiceProvider();

    private UserPortraitDeletionProcessor CreateProcessor(ServiceProvider services) => new(
        services.GetRequiredService<IServiceScopeFactory>(), m_S3.Object,
        Options.Create(new UserPortraitStorageConfig { Bucket = "test-bucket" }),
        NullLogger<UserPortraitDeletionProcessor>.Instance);

    [Test]
    public async Task ProcessPendingDeletionsAsync_FailedBatchDoesNotStarveNewerWork_Sqlite()
    {
        using var context = m_DbFactory.CreateDbContext<CharacterDbContext>();
        await using var services = CreateServices();
        await AssertFailedBatchDoesNotStarveNewerWorkAsync(context, services);
    }

    [Test]
    public async Task ProcessPendingDeletionsAsync_FailedBatchDoesNotStarveNewerWork_PostgreSql()
    {
        await using var database = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await database.StartAsync();
        var options = new DbContextOptionsBuilder<CharacterDbContext>()
            .UseNpgsql(database.GetConnectionString()).Options;
        await using var context = new CharacterDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await using var services = CreateServices(options);
        await AssertFailedBatchDoesNotStarveNewerWorkAsync(context, services);
    }

    private async Task AssertFailedBatchDoesNotStarveNewerWorkAsync(
        CharacterDbContext context, ServiceProvider services)
    {
        var createdAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var failures = Enumerable.Range(0, 100).Select(index => new UserPortraitDeletionModel
        {
            S3Key = $"failing/{index}",
            CreatedAtUtc = createdAt
        }).ToArray();
        var newer = new UserPortraitDeletionModel { S3Key = "newer", CreatedAtUtc = createdAt.AddDays(1) };
        context.UserPortraitDeletions.AddRange(failures);
        context.UserPortraitDeletions.Add(newer);
        await context.SaveChangesAsync();
        var attemptedKeys = new List<string>();
        m_S3.Setup(s3 => s3.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .Returns((DeleteObjectRequest request, CancellationToken _) =>
            {
                Assert.That(request.BucketName, Is.EqualTo("test-bucket"));
                attemptedKeys.Add(request.Key);
                return request.Key == newer.S3Key
                    ? Task.FromResult(new DeleteObjectResponse { HttpStatusCode = System.Net.HttpStatusCode.NoContent })
                    : Task.FromException<DeleteObjectResponse>(new AmazonS3Exception("permanent failure"));
            });
        var processor = CreateProcessor(services);

        await processor.ProcessPendingDeletionsAsync();

        Assert.That(attemptedKeys, Has.Count.EqualTo(100));
        Assert.That(attemptedKeys, Does.Not.Contain(newer.S3Key));
        context.ChangeTracker.Clear();
        var firstPass = await context.UserPortraitDeletions.AsNoTracking().ToListAsync();
        Assert.That(firstPass, Has.Count.EqualTo(101));
        Assert.That(firstPass.Where(row => row.Id != newer.Id).All(row =>
            row.Attempts == 1 && row.LastAttemptAtUtc.HasValue && row.LastError == "permanent failure" &&
            row.CreatedAtUtc == createdAt), Is.True);

        attemptedKeys.Clear();
        await processor.ProcessPendingDeletionsAsync();

        Assert.That(attemptedKeys, Has.Count.EqualTo(100));
        Assert.That(attemptedKeys[0], Is.EqualTo(newer.S3Key));
        var remaining = await context.UserPortraitDeletions.AsNoTracking().ToListAsync();
        Assert.That(remaining.Select(row => row.Id), Is.EquivalentTo(failures.Select(row => row.Id)));
        Assert.That(remaining.Count(row => row.Attempts == 2), Is.EqualTo(99));
        Assert.That(remaining.Count(row => row.Attempts == 1), Is.EqualTo(1));
        Assert.That(remaining.All(row => row.LastAttemptAtUtc.HasValue &&
            row.LastError == "permanent failure" && row.CreatedAtUtc == createdAt), Is.True);
    }

    [Test]
    public async Task ProcessPendingDeletionsAsync_EqualSchedulingTimes_UsesCreationThenId()
    {
        using var context = m_DbFactory.CreateDbContext<CharacterDbContext>();
        var schedulingTime = new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var rows = new[]
        {
            new UserPortraitDeletionModel { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), S3Key = "second", CreatedAtUtc = schedulingTime },
            new UserPortraitDeletionModel { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), S3Key = "first", CreatedAtUtc = schedulingTime },
            new UserPortraitDeletionModel { S3Key = "old-retry", CreatedAtUtc = schedulingTime.AddDays(-1), LastAttemptAtUtc = schedulingTime, Attempts = 1 }
        };
        context.UserPortraitDeletions.AddRange(rows);
        await context.SaveChangesAsync();
        var attemptedKeys = new List<string>();
        m_S3.Setup(s3 => s3.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .Returns((DeleteObjectRequest request, CancellationToken _) =>
            {
                attemptedKeys.Add(request.Key);
                return Task.FromResult(new DeleteObjectResponse { HttpStatusCode = System.Net.HttpStatusCode.NoContent });
            });
        await using var services = CreateServices();

        await CreateProcessor(services).ProcessPendingDeletionsAsync();

        Assert.That(attemptedKeys, Is.EqualTo(new[] { "old-retry", "first", "second" }));
    }

    [Test]
    public async Task ProcessPendingDeletionAsync_MissingRow_ReturnsTrueWithoutStorageCall()
    {
        using var context = m_DbFactory.CreateDbContext<CharacterDbContext>();
        await using var services = CreateServices();

        Assert.That(await CreateProcessor(services).ProcessPendingDeletionAsync(Guid.NewGuid()), Is.True);

        m_S3.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ProcessPendingDeletionAsync_StorageSucceededButCleanupFailed_RetriesSameKey()
    {
        using var context = m_DbFactory.CreateDbContext<CharacterDbContext>();
        var row = new UserPortraitDeletionModel { S3Key = "already-deleted" };
        context.UserPortraitDeletions.Add(row);
        await context.SaveChangesAsync();
        m_S3.Setup(s3 => s3.DeleteObjectAsync(It.Is<DeleteObjectRequest>(request => request.Key == row.S3Key),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteObjectResponse { HttpStatusCode = System.Net.HttpStatusCode.NoContent });
        await using var services = CreateServices(new FailFirstCleanup());
        var processor = CreateProcessor(services);

        Assert.That(await processor.ProcessPendingDeletionAsync(row.Id), Is.False);

        context.ChangeTracker.Clear();
        var retained = await context.UserPortraitDeletions.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(retained.Attempts, Is.EqualTo(1));
            Assert.That(retained.LastAttemptAtUtc, Is.Not.Null);
            Assert.That(retained.LastError, Is.EqualTo("cleanup failed"));
        });
        Assert.That(await processor.ProcessPendingDeletionAsync(row.Id), Is.True);
        Assert.That(await context.UserPortraitDeletions.AnyAsync(), Is.False);
        m_S3.Verify(s3 => s3.DeleteObjectAsync(It.Is<DeleteObjectRequest>(request => request.Key == row.S3Key),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Test]
    public async Task ProcessPendingDeletionsAsync_CancellationDuringStorage_StopsBatchWithoutRecordingFailure()
    {
        using var context = m_DbFactory.CreateDbContext<CharacterDbContext>();
        context.UserPortraitDeletions.AddRange(new UserPortraitDeletionModel { S3Key = "one" },
            new UserPortraitDeletionModel { S3Key = "two" });
        await context.SaveChangesAsync();
        using var cancellation = new CancellationTokenSource();
        m_S3.Setup(s3 => s3.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), cancellation.Token))
            .Returns((DeleteObjectRequest _, CancellationToken token) =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<DeleteObjectResponse>(token);
            });
        await using var services = CreateServices();

        await Assert.CatchAsync<OperationCanceledException>(() =>
            CreateProcessor(services).ProcessPendingDeletionsAsync(cancellation.Token));

        var remaining = await context.UserPortraitDeletions.AsNoTracking().ToListAsync();
        Assert.That(remaining, Has.Count.EqualTo(2));
        Assert.That(remaining.All(row => row.Attempts == 0 && row.LastAttemptAtUtc == null && row.LastError == null), Is.True);
        m_S3.Verify(s3 => s3.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), cancellation.Token), Times.Once);
    }

    private sealed class FailFirstCleanup : SaveChangesInterceptor
    {
        private bool m_Failed;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!m_Failed && eventData.Context!.ChangeTracker.Entries<UserPortraitDeletionModel>()
                    .Any(entry => entry.State == EntityState.Deleted))
            {
                m_Failed = true;
                throw new IOException("cleanup failed");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
