using Amazon.S3;
using Amazon.S3.Model;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Infrastructure.Character;
using Mehrak.Infrastructure.Character.Models;
using Mehrak.Infrastructure.Character.Services;
using Mehrak.Infrastructure.Shared.Config;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Testcontainers.PostgreSql;

namespace Mehrak.Infrastructure.Tests.Character.Services;

[TestFixture]
[NonParallelizable]
internal sealed class UserPortraitPostgreSqlConcurrencyTests
{
    private PostgreSqlContainer m_Container = null!;
    private ServiceProvider m_ServiceProvider = null!;
    private DbContextOptions<CharacterDbContext> m_ContextOptions = null!;
    private Mock<IAmazonS3> m_S3 = null!;
    private int m_MigratedAliasConflictCount;
    private int m_MigratedActivePortraitCount;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        m_Container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await m_Container.StartAsync();

        m_ContextOptions = new DbContextOptionsBuilder<CharacterDbContext>()
            .UseNpgsql(m_Container.GetConnectionString())
            .Options;
        await using (var context = new CharacterDbContext(m_ContextOptions))
        {
            await context.Database.MigrateAsync("20260805025848_AddPortraitConfigSubId");
            await context.Database.ExecuteSqlRawAsync("INSERT INTO \"Aliases\" (\"Game\", \"Alias\", \"CharacterName\") VALUES (1, 'raiden', 'Raiden Shogun'), (1, 'RAIDEN', 'Raiden')");
            context.UserPortraitUploads.AddRange(
                new UserPortraitUpload
                {
                    Id = Guid.NewGuid(),
                    DiscordUserId = 100L,
                    Game = Game.Genshin,
                    CharacterName = "Raiden",
                    SHA256Hash = "migration-one",
                    S3Key = "100/migration-one.png",
                    IsActive = true
                },
                new UserPortraitUpload
                {
                    Id = Guid.NewGuid(),
                    DiscordUserId = 100L,
                    Game = Game.Genshin,
                    CharacterName = "Raiden",
                    SHA256Hash = "migration-two",
                    S3Key = "100/migration-two.png",
                    IsActive = true
                });
            await context.SaveChangesAsync();
            await context.Database.MigrateAsync();

            m_MigratedAliasConflictCount = await context.AliasConflicts.CountAsync();
            m_MigratedActivePortraitCount = await context.UserPortraitUploads.CountAsync(upload => upload.IsActive);
        }

        var services = new ServiceCollection();
        services.AddScoped(_ => new CharacterDbContext(m_ContextOptions));
        m_ServiceProvider = services.BuildServiceProvider();

        m_S3 = new Mock<IAmazonS3>();
        m_S3.Setup(s => s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutObjectResponse { HttpStatusCode = System.Net.HttpStatusCode.OK });
        m_S3.Setup(s => s.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteObjectResponse { HttpStatusCode = System.Net.HttpStatusCode.NoContent });
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await m_ServiceProvider.DisposeAsync();
        await m_Container.DisposeAsync();
    }

    [SetUp]
    public async Task SetUp()
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"UserPortraitDeletions\", \"UserPortraitConfigs\", \"UserPortraitUploads\", \"Characters\" RESTART IDENTITY CASCADE");
        context.Characters.Add(new CharacterModel { Game = Game.Genshin, Name = "Raiden" });
        await context.SaveChangesAsync();
    }

    [Test]
    public void MigrationChain_ReconcilesLegacyCollisionsBeforeApplyingConstraints()
    {
        Assert.Multiple(() =>
        {
            Assert.That(m_MigratedAliasConflictCount, Is.EqualTo(1));
            Assert.That(m_MigratedActivePortraitCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ConcurrentUploadsAcrossIndependentContexts_RespectFivePortraitQuota()
    {
        var services = Enumerable.Range(0, 6)
            .Select(_ => CreateService())
            .ToArray();

        var results = await Task.WhenAll(services.Select((service, index) => service.UploadPortraitAsync(
            100L, Game.Genshin, "Raiden", new MemoryStream(), $"hash-{index}", "png")));

        await using var context = CreateContext();
        var count = await context.UserPortraitUploads.CountAsync();
        var activeCount = await context.UserPortraitUploads.CountAsync(upload => upload.IsActive);

        Assert.Multiple(() =>
        {
            Assert.That(results.Count(result => result.Succeeded), Is.EqualTo(5));
            Assert.That(count, Is.EqualTo(5));
            Assert.That(activeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task CompetingActivationAcrossIndependentContexts_LeavesExactlyOneActivePortrait()
    {
        Guid firstId;
        Guid secondId;
        await using (var context = CreateContext())
        {
            var first = SeedPortrait(context, "first");
            var second = SeedPortrait(context, "second");
            context.UserPortraitUploads.AddRange(first, second);
            await context.SaveChangesAsync();
            firstId = first.Id;
            secondId = second.Id;
        }

        var services = new[] { CreateService(), CreateService() };
        var results = await Task.WhenAll(
            services[0].SetActivePortraitAsync(100L, firstId),
            services[1].SetActivePortraitAsync(100L, secondId));

        await using var verifyContext = CreateContext();
        var active = await verifyContext.UserPortraitUploads.CountAsync(upload => upload.IsActive);

        Assert.Multiple(() =>
        {
            Assert.That(results, Is.All.True);
            Assert.That(active, Is.EqualTo(1));
        });
    }

    private UserPortraitService CreateService()
    {
        return new UserPortraitService(
            m_ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            m_S3.Object,
            Options.Create(new UserPortraitStorageConfig { Bucket = "test-bucket" }),
            NullLogger<UserPortraitService>.Instance);
    }

    private CharacterDbContext CreateContext() => new(m_ContextOptions);

    private static UserPortraitUpload SeedPortrait(CharacterDbContext context, string hash) =>
        new()
        {
            DiscordUserId = 100L,
            Game = Game.Genshin,
            CharacterName = "Raiden",
            SHA256Hash = hash,
            S3Key = $"100/{hash}.png",
            Config = new UserPortraitConfigModel { Id = Guid.NewGuid() }
        };
}
