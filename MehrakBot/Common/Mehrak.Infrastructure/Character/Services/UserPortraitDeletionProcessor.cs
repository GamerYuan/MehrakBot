using Amazon.S3;
using Amazon.S3.Model;
using Mehrak.Infrastructure.Character.Models;
using Mehrak.Infrastructure.Shared.Config;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mehrak.Infrastructure.Character.Services;

public sealed class UserPortraitDeletionProcessor
{
    private readonly IServiceScopeFactory m_ScopeFactory;
    private readonly IAmazonS3 m_S3;
    private readonly string m_Bucket;
    private readonly ILogger<UserPortraitDeletionProcessor> m_Logger;

    public UserPortraitDeletionProcessor(
        IServiceScopeFactory scopeFactory,
        IAmazonS3 s3,
        IOptions<UserPortraitStorageConfig> options,
        ILogger<UserPortraitDeletionProcessor> logger)
    {
        m_ScopeFactory = scopeFactory;
        m_S3 = s3;
        m_Bucket = options.Value.Bucket;
        m_Logger = logger;
    }

    public async Task ProcessPendingDeletionsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = m_ScopeFactory.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();
        var pendingIds = await context.UserPortraitDeletions
            .AsNoTracking()
            .OrderBy(deletion => deletion.CreatedAtUtc)
            .Take(100)
            .Select(deletion => deletion.Id)
            .ToListAsync(cancellationToken);

        foreach (var pendingId in pendingIds)
            await ProcessPendingDeletionAsync(pendingId, cancellationToken);

    }

    public async Task<bool> ProcessPendingDeletionAsync(Guid deletionId, CancellationToken cancellationToken = default)
    {
        UserPortraitDeletionModel? deletion;
        using (var scope = m_ScopeFactory.CreateScope())
        using (var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>())
        {
            deletion = await context.UserPortraitDeletions
                .AsNoTracking()
                .SingleOrDefaultAsync(entry => entry.Id == deletionId, cancellationToken);
        }

        if (deletion == null)
            return true;

        try
        {
            var response = await m_S3.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = m_Bucket,
                Key = deletion.S3Key
            }, cancellationToken);

            if ((int)response.HttpStatusCode >= 300)
                throw new AmazonS3Exception($"S3 returned status {response.HttpStatusCode}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await RecordFailureAsync(deletionId, exception, cancellationToken);
            return false;
        }

        try
        {
            using var scope = m_ScopeFactory.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            var outboxEntry = await context.UserPortraitDeletions
                .SingleOrDefaultAsync(entry => entry.Id == deletionId, cancellationToken);
            if (outboxEntry != null)
                context.UserPortraitDeletions.Remove(outboxEntry);

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await RecordFailureAsync(deletionId, exception, cancellationToken);
            m_Logger.LogError(exception, "Storage deletion completed but database cleanup is pending for {DeletionId}", deletionId);
            return false;
        }
    }

    private async Task RecordFailureAsync(Guid deletionId, Exception exception, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = m_ScopeFactory.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();
            var entry = await context.UserPortraitDeletions
                .SingleOrDefaultAsync(deletion => deletion.Id == deletionId, cancellationToken);
            if (entry == null)
                return;

            entry.Attempts++;
            entry.LastAttemptAtUtc = DateTime.UtcNow;
            entry.LastError = exception.Message[..Math.Min(exception.Message.Length, 1000)];
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception recordException) when (recordException is not OperationCanceledException)
        {
            m_Logger.LogError(recordException, "Failed to record pending portrait deletion failure for {DeletionId}", deletionId);
        }

        m_Logger.LogWarning(exception, "Portrait storage deletion remains pending for {DeletionId}", deletionId);
    }
}
