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
    private static readonly TimeSpan UploadIntentRecoveryAge = TimeSpan.FromHours(1);

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

        await ProcessPendingUploadIntentsAsync(cancellationToken);
    }

    public async Task ProcessPendingUploadIntentsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = m_ScopeFactory.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();
        var cutoff = DateTime.UtcNow - UploadIntentRecoveryAge;
        var pendingIds = await context.UserPortraitUploadIntents
            .AsNoTracking()
            .Where(intent => intent.CreatedAtUtc <= cutoff)
            .OrderBy(intent => intent.CreatedAtUtc)
            .Take(100)
            .Select(intent => intent.Id)
            .ToListAsync(cancellationToken);

        foreach (var pendingId in pendingIds)
            await ProcessPendingUploadIntentAsync(pendingId, cancellationToken);
    }

    public async Task<bool> ProcessPendingUploadIntentAsync(
        Guid intentId, CancellationToken cancellationToken = default)
    {
        UserPortraitUploadIntentModel? discoveredIntent;
        using (var scope = m_ScopeFactory.CreateScope())
        using (var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>())
        {
            discoveredIntent = await context.UserPortraitUploadIntents
                .AsNoTracking()
                .SingleOrDefaultAsync(intent => intent.Id == intentId, cancellationToken);
        }

        if (discoveredIntent == null)
            return true;

        using var processingScope = m_ScopeFactory.CreateScope();
        using var processingContext = processingScope.ServiceProvider.GetRequiredService<CharacterDbContext>();
        await using var operationLock = await CharacterDbLock.AcquireSessionAsync(
            processingContext, $"portrait-upload:{intentId}", cancellationToken);
        await using var transaction = await processingContext.Database.BeginTransactionAsync(cancellationToken);
        await CharacterDbLock.AcquireAsync(processingContext,
            $"portrait:{discoveredIntent.DiscordUserId}:{discoveredIntent.Game}:{discoveredIntent.CharacterName}",
            cancellationToken);

        var intent = await processingContext.UserPortraitUploadIntents
            .SingleOrDefaultAsync(entry => entry.Id == intentId, cancellationToken);
        if (intent == null)
            return true;

        var uploadExists = await processingContext.UserPortraitUploads.AnyAsync(upload =>
            upload.S3Key == intent.S3Key,
            cancellationToken);
        if (uploadExists)
        {
            processingContext.UserPortraitUploadIntents.Remove(intent);
            await processingContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        if (intent.CreatedAtUtc > DateTime.UtcNow - UploadIntentRecoveryAge)
            return false;

        try
        {
            var response = await m_S3.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = m_Bucket,
                Key = intent.S3Key
            }, cancellationToken);

            if ((int)response.HttpStatusCode >= 300)
                throw new AmazonS3Exception($"S3 returned status {response.HttpStatusCode}.");

            processingContext.UserPortraitUploadIntents.Remove(intent);
            await processingContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            intent.Attempts++;
            intent.LastAttemptAtUtc = DateTime.UtcNow;
            intent.LastError = exception.Message[..Math.Min(exception.Message.Length, 1000)];
            await processingContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            m_Logger.LogWarning(exception, "Portrait upload intent remains pending for {IntentId}", intentId);
            return false;
        }
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

            if (deletion.UserPortraitUploadId is Guid uploadId)
            {
                var upload = await context.UserPortraitUploads
                    .SingleOrDefaultAsync(entry => entry.Id == uploadId, cancellationToken);
                if (upload != null)
                    context.UserPortraitUploads.Remove(upload);
            }

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
