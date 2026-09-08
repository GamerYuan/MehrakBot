using System.Data.Common;
using Amazon.S3.Model;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Infrastructure.Character;
using Mehrak.Infrastructure.Character.Models;
using Mehrak.Infrastructure.Character.Services;
using Mehrak.Infrastructure.Shared.Config;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;

namespace Mehrak.Infrastructure.Tests.Character.Services;

internal sealed partial class UserPortraitPostgreSqlConcurrencyTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task UploadCommitFailure_ReconcilesTheActualCommitOutcome(bool acknowledgementLost)
    {
        var fault = new UploadCommitFault(acknowledgementLost);
        var options = new DbContextOptionsBuilder<CharacterDbContext>(m_ContextOptions)
            .AddInterceptors(fault).Options;
        await using var services = new ServiceCollection()
            .AddScoped(_ => new CharacterDbContext(options)).BuildServiceProvider();
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        var service = new UserPortraitService(scopeFactory, m_S3.Object,
            Options.Create(new UserPortraitStorageConfig { Bucket = "test-bucket" }),
            NullLogger<UserPortraitService>.Instance);

        var result = await service.UploadPortraitAsync(100, Game.Genshin, "Raiden",
            new MemoryStream(), "commit-fault", "png");
        Assert.That(fault.Injected, Is.True);
        Assert.That(result.Succeeded, Is.EqualTo(acknowledgementLost));

        await using (var context = CreateContext())
        {
            Assert.That(await context.UserPortraitUploads.CountAsync(), Is.EqualTo(acknowledgementLost ? 1 : 0));
            foreach (var intent in await context.UserPortraitUploadIntents.ToListAsync())
                intent.CreatedAtUtc = DateTime.UtcNow.AddHours(-2);
            await context.SaveChangesAsync();
        }

        var processor = CreateRecoveryProcessor();
        await processor.ProcessPendingUploadIntentsAsync();
        m_S3.Verify(s3 => s3.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()),
            acknowledgementLost ? Times.Never() : Times.Once());
        await using var verify = CreateContext();
        Assert.That(await verify.UserPortraitUploadIntents.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task Recovery_DeletesOnlyTheIntentObjectWhenAnotherUploadHasTheSameHash()
    {
        await using (var context = CreateContext())
        {
            context.UserPortraitUploads.Add(SeedPortrait(context, "same-hash"));
            context.UserPortraitUploadIntents.Add(new UserPortraitUploadIntentModel
            {
                DiscordUserId = 100, Game = Game.Genshin, CharacterName = "Raiden",
                SHA256Hash = "same-hash", S3Key = "100/orphan.png", CreatedAtUtc = DateTime.UtcNow.AddHours(-2)
            });
            await context.SaveChangesAsync();
        }
        await CreateRecoveryProcessor().ProcessPendingUploadIntentsAsync();
        m_S3.Verify(s3 => s3.DeleteObjectAsync(It.Is<DeleteObjectRequest>(request => request.Key == "100/orphan.png"),
            It.IsAny<CancellationToken>()), Times.Once);
        m_S3.Verify(s3 => s3.DeleteObjectAsync(It.Is<DeleteObjectRequest>(request => request.Key != "100/orphan.png"),
            It.IsAny<CancellationToken>()), Times.Never);
        await using var verify = CreateContext();
        Assert.That(await verify.UserPortraitUploads.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public void ReconciliationMigration_HasSchemaMetadataAndBlocksDestructiveRollback()
    {
        using var context = CreateContext();
        var assembly = context.GetService<IMigrationsAssembly>();
        var metadata = assembly.Migrations["20260907112450_ReconcileCharacterData"];
        var migration = assembly.CreateMigration(metadata, context.Database.ProviderName!);
        Assert.That(migration.TargetModel.FindEntityType(typeof(AliasConflictModel).FullName!), Is.Not.Null);
        Assert.That(migration.TargetModel.FindEntityType(typeof(UserPortraitUploadIntentModel).FullName!), Is.Null);
        Assert.That(migration.TargetModel.FindEntityType(typeof(UserPortraitUpload).FullName!)!.GetIndexes()
            .Any(index => index.GetFilter() != null), Is.False);
        Assert.Throws<InvalidOperationException>(() => context.GetService<IMigrator>().GenerateScript(
            "20260907112450_ReconcileCharacterData", "20260907112437_AuditContentPersistence"));
    }

    private UserPortraitDeletionProcessor CreateRecoveryProcessor() => new(
        m_ServiceProvider.GetRequiredService<IServiceScopeFactory>(), m_S3.Object,
        Options.Create(new UserPortraitStorageConfig { Bucket = "test-bucket" }),
        NullLogger<UserPortraitDeletionProcessor>.Instance);

    [Test]
    public async Task OlderCacheRefresh_CannotOverwriteAConcurrentCharacterWrite()
    {
        var firstPublishStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstPublish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publications = 0;
        string[] published = [];
        m_RedisDatabase.Setup(database => database.CreateTransaction(It.IsAny<object>())).Returns(() =>
        {
            string[] snapshot = [];
            var transaction = new Mock<ITransaction>();
            transaction.Setup(value => value.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(true);
            transaction.Setup(value => value.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
                .Callback<RedisKey, RedisValue[], CommandFlags>((_, values, _) => snapshot = values.Select(value => value.ToString()).ToArray())
                .ReturnsAsync(1L);
            transaction.Setup(value => value.ExecuteAsync(It.IsAny<CommandFlags>())).Returns(async () =>
            {
                if (Interlocked.Increment(ref publications) == 1)
                {
                    firstPublishStarted.TrySetResult();
                    await releaseFirstPublish.Task;
                }
                published = snapshot;
                return true;
            });
            return transaction.Object;
        });
        var oldRefresh = CreateCharacterCacheService().UpdateCharactersAsync(Game.Genshin);
        await firstPublishStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var newWrite = CreateCharacterCacheService().UpsertCharacters(Game.Genshin, ["Furina"]);
        try
        {
            Assert.That(await Task.WhenAny(newWrite, Task.Delay(200)), Is.Not.SameAs(newWrite));
        }
        finally
        {
            releaseFirstPublish.TrySetResult();
        }
        await Task.WhenAll(oldRefresh, newWrite).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(published, Does.Contain("Furina"));
    }

    private sealed class UploadCommitFault(bool acknowledgementLost) : DbTransactionInterceptor
    {
        private int m_Commits;
        public bool Injected { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (++m_Commits == 2 && !acknowledgementLost)
            {
                Injected = true;
                throw new IOException("Injected failure before upload commit.");
            }
            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (m_Commits == 2 && acknowledgementLost)
            {
                Injected = true;
                throw new IOException("Injected lost commit acknowledgement.");
            }
            return Task.CompletedTask;
        }
    }
}
