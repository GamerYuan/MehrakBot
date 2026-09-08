using Amazon.S3;
using Amazon.S3.Model;
using Mehrak.Domain.Character;
using Mehrak.Domain.Character.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Services;
using Mehrak.Infrastructure.Character.Models;
using Mehrak.Infrastructure.Shared.Config;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Mehrak.Infrastructure.Character.Services;

internal class UserPortraitService : IUserPortraitService
{
    private const int MaxPortraitsPerCharacter = 5;

    private readonly IServiceScopeFactory m_ScopeFactory;
    private readonly IAmazonS3 m_S3;
    private readonly string m_Bucket;
    private readonly ILogger<UserPortraitService> m_Logger;
    private readonly UserPortraitDeletionProcessor m_DeletionProcessor;

    public UserPortraitService(
        IServiceScopeFactory scopeFactory,
        IAmazonS3 s3,
        IOptions<UserPortraitStorageConfig> options,
        ILogger<UserPortraitService> logger,
        UserPortraitDeletionProcessor? deletionProcessor = null)
    {
        m_ScopeFactory = scopeFactory;
        m_S3 = s3;
        m_Bucket = options.Value.Bucket;
        m_Logger = logger;
        m_DeletionProcessor = deletionProcessor ?? new UserPortraitDeletionProcessor(
            scopeFactory, s3, options, NullLogger<UserPortraitDeletionProcessor>.Instance);
    }

    public async Task<IReadOnlyCollection<UserPortraitUploadDto>> GetUserPortraitsAsync(
        long discordUserId, Game game, string? characterName, CancellationToken ct = default)
    {
        using var scope = m_ScopeFactory.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

        var query = context.UserPortraitUploads
            .AsNoTracking()
            .Include(u => u.Config)
            .Where(u => u.DiscordUserId == discordUserId && u.Game == game &&
                        !context.UserPortraitDeletions.Any(deletion => deletion.UserPortraitUploadId == u.Id));

        if (!string.IsNullOrWhiteSpace(characterName))
        {
            var normalized = characterName.ReplaceLineEndings("").Trim();
            query = query.Where(u => u.CharacterName == normalized);
        }

        var uploads = await query
            .OrderBy(u => u.CharacterName)
            .ThenBy(u => u.CreatedAt)
            .ToListAsync(ct);

        return uploads.Select(ToDto).ToList();
    }

    public async Task<UserPortraitUploadDto?> GetPortraitAsync(
        long discordUserId, Guid uploadId, CancellationToken ct = default)
    {
        using var scope = m_ScopeFactory.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

        var entity = await context.UserPortraitUploads
            .AsNoTracking()
            .Include(u => u.Config)
            .FirstOrDefaultAsync(u => u.Id == uploadId && u.DiscordUserId == discordUserId &&
                                      !context.UserPortraitDeletions.Any(deletion => deletion.UserPortraitUploadId == u.Id), ct);

        return entity == null ? null : ToDto(entity);
    }

    public async Task<AttachmentDownloadResult?> GetPortraitImageAsync(
        long discordUserId, Guid uploadId, CancellationToken ct = default)
    {
        using var scope = m_ScopeFactory.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

        var entity = await context.UserPortraitUploads
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == uploadId && u.DiscordUserId == discordUserId &&
                                      !context.UserPortraitDeletions.Any(deletion => deletion.UserPortraitUploadId == u.Id), ct);

        if (entity == null)
            return null;

        try
        {
            var getReq = new GetObjectRequest
            {
                BucketName = m_Bucket,
                Key = entity.S3Key
            };

            using var response = await m_S3.GetObjectAsync(getReq, ct);

            if ((int)response.HttpStatusCode >= 300)
                return null;

            var stream = new MemoryStream();
            await response.ResponseStream.CopyToAsync(stream, ct);
            stream.Position = 0;

            var contentType = ResolveContentType(entity.S3Key);
            return new AttachmentDownloadResult(stream, contentType);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Failed to retrieve portrait image from S3: {S3Key}", entity.S3Key);
            return null;
        }
    }

    public async Task<AttachmentDownloadResult?> GetPortraitImageAsync(
        long discordUserId, string s3Key, Guid uploadId, CancellationToken ct = default)
    {
        // Validate ownership before S3 fetch
        using var scope = m_ScopeFactory.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

        var exists = await context.UserPortraitUploads
            .AsNoTracking()
            .AnyAsync(u => u.Id == uploadId && u.DiscordUserId == discordUserId && u.S3Key == s3Key &&
                           !context.UserPortraitDeletions.Any(deletion => deletion.UserPortraitUploadId == u.Id), ct);

        if (!exists)
            return null;

        try
        {
            var getReq = new GetObjectRequest
            {
                BucketName = m_Bucket,
                Key = s3Key
            };

            using var response = await m_S3.GetObjectAsync(getReq, ct);

            if ((int)response.HttpStatusCode >= 300)
                return null;

            var stream = new MemoryStream();
            await response.ResponseStream.CopyToAsync(stream, ct);
            stream.Position = 0;

            var contentType = ResolveContentType(s3Key);
            return new AttachmentDownloadResult(stream, contentType);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Failed to retrieve portrait image from S3: {S3Key}", s3Key);
            return null;
        }
    }

    public async Task<UploadPortraitResult> UploadPortraitAsync(
        long discordUserId, Game game, string characterName, Stream imageStream, string sha256, string extension, CancellationToken ct = default)
    {
        var normalizedCharacter = characterName.ReplaceLineEndings("").Trim();

        using var scope = m_ScopeFactory.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        await CharacterDbLock.AcquireAsync(context,
            $"portrait:{discordUserId}:{game}:{normalizedCharacter}", ct);

        // Validate character exists
        var characterExists = await context.Characters
            .AnyAsync(c => c.Game == game && c.Name == normalizedCharacter, ct);

        if (!characterExists)
        {
            return new UploadPortraitResult
            {
                Succeeded = false,
                Error = "Character not found."
            };
        }

        // Check upload count
        var existingCount = await context.UserPortraitUploads
            .CountAsync(u =>
                u.DiscordUserId == discordUserId &&
                u.Game == game &&
                u.CharacterName == normalizedCharacter, ct);

        var pendingIntentCount = await context.UserPortraitUploadIntents
            .CountAsync(i =>
                i.DiscordUserId == discordUserId &&
                i.Game == game &&
                i.CharacterName == normalizedCharacter, ct);

        if (existingCount + pendingIntentCount >= MaxPortraitsPerCharacter)
        {
            return new UploadPortraitResult
            {
                Succeeded = false,
                Error = $"Maximum of {MaxPortraitsPerCharacter} portraits per character reached."
            };
        }

        // Check duplicate
        var duplicateExists = await context.UserPortraitUploads
            .AnyAsync(u =>
                u.DiscordUserId == discordUserId &&
                u.Game == game &&
                u.CharacterName == normalizedCharacter &&
                u.SHA256Hash == sha256, ct);

        if (duplicateExists)
        {
            return new UploadPortraitResult
            {
                Succeeded = false,
                Error = "This image has already been uploaded for this character."
            };
        }

        var pendingDuplicateExists = await context.UserPortraitUploadIntents
            .AnyAsync(i =>
                i.DiscordUserId == discordUserId &&
                i.Game == game &&
                i.CharacterName == normalizedCharacter &&
                i.SHA256Hash == sha256, ct);

        if (pendingDuplicateExists)
        {
            return new UploadPortraitResult
            {
                Succeeded = false,
                Error = "This image is already being uploaded for this character."
            };
        }

        var uploadId = Guid.CreateVersion7();
        var s3Key = $"{discordUserId}/{uploadId}.{extension}";
        var contentType = extension == "png" ? "image/png" : "image/jpeg";

        var intent = new UserPortraitUploadIntentModel
        {
            DiscordUserId = discordUserId,
            Game = game,
            CharacterName = normalizedCharacter,
            SHA256Hash = sha256,
            S3Key = s3Key
        };
        context.UserPortraitUploadIntents.Add(intent);
        // Exclude cleanup while this upload is in flight, independently of
        // the character transaction used to reserve and finalize its record.
        using var operationScope = m_ScopeFactory.CreateScope();
        using var operationContext = operationScope.ServiceProvider.GetRequiredService<CharacterDbContext>();
        await using var operationLock = await CharacterDbLock.AcquireSessionAsync(
            operationContext, $"portrait-upload:{intent.Id}", ct);

        try
        {
            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            m_Logger.LogError(exception, "Failed to create portrait upload intent");
            return new UploadPortraitResult
            {
                Succeeded = false,
                Error = "Failed to reserve upload. Please try again later."
            };
        }
        finally
        {
            // End a failed reservation transaction before releasing the session lock.
            await transaction.DisposeAsync();
        }

        if (imageStream.CanSeek) imageStream.Position = 0;

        var putReq = new PutObjectRequest
        {
            BucketName = m_Bucket,
            Key = s3Key,
            InputStream = imageStream,
            AutoCloseStream = false,
            ContentType = contentType
        };

        PutObjectResponse response;
        try
        {
            response = await m_S3.PutObjectAsync(putReq, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            m_Logger.LogWarning("Portrait upload was cancelled after reserving storage intent: {S3Key}", s3Key);
            throw;
        }
        catch (Exception exception)
        {
            m_Logger.LogWarning(exception, "Portrait storage upload outcome is unknown: {S3Key}", s3Key);
            return new UploadPortraitResult
            {
                Succeeded = false,
                Error = "Failed to upload image to storage. It will be cleaned up automatically if necessary."
            };
        }

        if ((int)response.HttpStatusCode >= 300)
        {
            m_Logger.LogError("Failed to upload portrait to S3. Status: {StatusCode}", response.HttpStatusCode);
            return new UploadPortraitResult
            {
                Succeeded = false,
                Error = "Failed to upload image to storage. It will be cleaned up automatically if necessary."
            };
        }

        UserPortraitUpload? upload;
        try
        {
            upload = await FinalizeUploadAsync(
                intent.Id, uploadId, discordUserId, game, normalizedCharacter, sha256, s3Key, ct);

            if (upload == null)
            {
                upload = await RecoverUploadIntentAsync(
                    intent.Id,
                    uploadId,
                    discordUserId,
                    game,
                    normalizedCharacter,
                    sha256,
                    s3Key,
                    new InvalidOperationException("Portrait upload intent disappeared before finalization."));
            }
        }
        catch (Exception exception)
        {
            upload = await RecoverUploadIntentAsync(
                intent.Id, uploadId, discordUserId, game, normalizedCharacter, sha256, s3Key, exception);

            if (upload == null && exception is OperationCanceledException)
                throw;
        }

        if (upload == null)
        {
            return new UploadPortraitResult
            {
                Succeeded = false,
                Error = "Failed to save upload record. The storage operation will be reconciled automatically."
            };
        }

        m_Logger.LogInformation("Portrait uploaded: {UploadId} for {DiscordUserId} - {Game}/{Character}",
            upload.Id, discordUserId, game, normalizedCharacter);

        return new UploadPortraitResult
        {
            Succeeded = true,
            UploadId = upload.Id,
            Portrait = ToDto(upload)
        };
    }

    private async Task<UserPortraitUpload?> FinalizeUploadAsync(
        Guid intentId,
        Guid uploadId,
        long discordUserId,
        Game game,
        string characterName,
        string sha256,
        string s3Key,
        CancellationToken cancellationToken)
    {
        using var scope = m_ScopeFactory.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        await CharacterDbLock.AcquireAsync(context,
            $"portrait:{discordUserId}:{game}:{characterName}", cancellationToken);

        var existingUpload = await context.UserPortraitUploads
            .Include(upload => upload.Config)
            .SingleOrDefaultAsync(upload =>
                upload.Id == uploadId ||
                (upload.DiscordUserId == discordUserId && upload.Game == game &&
                 upload.CharacterName == characterName && upload.SHA256Hash == sha256), cancellationToken);
        var intent = await context.UserPortraitUploadIntents
            .SingleOrDefaultAsync(entry => entry.Id == intentId, cancellationToken);

        if (existingUpload != null)
        {
            if (intent != null)
            {
                if (!string.Equals(existingUpload.S3Key, s3Key, StringComparison.Ordinal))
                {
                    context.UserPortraitDeletions.Add(new UserPortraitDeletionModel
                    {
                        S3Key = s3Key
                    });
                }

                context.UserPortraitUploadIntents.Remove(intent);
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return existingUpload;
        }

        if (intent == null)
            return null;

        var isFirstForCharacter = !await context.UserPortraitUploads.AnyAsync(upload =>
            upload.DiscordUserId == discordUserId &&
            upload.Game == game &&
            upload.CharacterName == characterName, cancellationToken);

        var upload = new UserPortraitUpload
        {
            Id = uploadId,
            DiscordUserId = discordUserId,
            Game = game,
            CharacterName = characterName,
            SHA256Hash = sha256,
            S3Key = s3Key,
            IsActive = isFirstForCharacter,
            Config = new UserPortraitConfigModel
            {
                Id = Guid.NewGuid()
            }
        };

        context.UserPortraitUploads.Add(upload);
        context.UserPortraitUploadIntents.Remove(intent);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return upload;
    }

    private async Task<UserPortraitUpload?> RecoverUploadIntentAsync(
        Guid intentId,
        Guid uploadId,
        long discordUserId,
        Game game,
        string characterName,
        string sha256,
        string s3Key,
        Exception failure)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var scope = m_ScopeFactory.CreateScope();
                using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();
                await using var transaction = await context.Database.BeginTransactionAsync(CancellationToken.None);

                await CharacterDbLock.AcquireAsync(context,
                    $"portrait:{discordUserId}:{game}:{characterName}", CancellationToken.None);

                var upload = await context.UserPortraitUploads
                    .Include(entity => entity.Config)
                    .SingleOrDefaultAsync(entity => entity.Id == uploadId, CancellationToken.None);
                var intent = await context.UserPortraitUploadIntents
                    .SingleOrDefaultAsync(entity => entity.Id == intentId, CancellationToken.None);

                if (upload != null)
                {
                    if (intent != null)
                        context.UserPortraitUploadIntents.Remove(intent);

                    await context.SaveChangesAsync(CancellationToken.None);
                    await transaction.CommitAsync(CancellationToken.None);
                    return upload;
                }

                intent ??= new UserPortraitUploadIntentModel
                {
                    Id = intentId,
                    DiscordUserId = discordUserId,
                    Game = game,
                    CharacterName = characterName,
                    SHA256Hash = sha256,
                    S3Key = s3Key
                };

                intent.Attempts++;
                intent.LastAttemptAtUtc = DateTime.UtcNow;
                intent.LastError = failure.Message[..Math.Min(failure.Message.Length, 1000)];
                if (context.Entry(intent).State == EntityState.Detached)
                    context.UserPortraitUploadIntents.Add(intent);

                await context.SaveChangesAsync(CancellationToken.None);
                await transaction.CommitAsync(CancellationToken.None);
                return null;
            }
            catch (Exception exception) when (attempt < 3)
            {
                m_Logger.LogWarning(exception, "Portrait upload recovery attempt {Attempt} failed for {S3Key}", attempt, s3Key);
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), CancellationToken.None);
            }
            catch (Exception exception)
            {
                m_Logger.LogCritical(exception, "Could not durably recover portrait upload intent for {S3Key}", s3Key);
            }
        }

        return null;
    }

    public async Task<bool> UpdatePortraitConfigAsync(
        long discordUserId, Guid uploadId, UserPortraitConfigDto config, CancellationToken ct = default)
    {
        using var scope = m_ScopeFactory.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

        var entity = await context.UserPortraitUploads
            .Include(u => u.Config)
            .FirstOrDefaultAsync(u => u.Id == uploadId && u.DiscordUserId == discordUserId &&
                                      !context.UserPortraitDeletions.Any(deletion => deletion.UserPortraitUploadId == u.Id), ct);

        if (entity == null)
            return false;

        if (config.ArtistAttribution is { Length: > 15 })
        {
            m_Logger.LogWarning("ArtistAttribution exceeds 15 characters for UploadId {UploadId}", uploadId);
            return false;
        }

        // Defense in depth: the API validates first, but stored scales must stay finite
        // and bounded no matter which caller reaches this service.
        if (config.TargetScale is float scale &&
            (!float.IsFinite(scale) || scale < 0.01f || scale > 10f))
        {
            m_Logger.LogWarning("TargetScale out of range for UploadId {UploadId}", uploadId);
            return false;
        }

        entity.Config.OffsetX = config.OffsetX;
        entity.Config.OffsetY = config.OffsetY;
        entity.Config.TargetScale = config.TargetScale;
        entity.Config.FlipX = config.FlipX;
        entity.Config.ArtistAttribution = config.ArtistAttribution;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        try
        {
            await context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException e)
        {
            m_Logger.LogError(e, "Failed to update portrait config for {UploadId}", uploadId);
            return false;
        }
    }

    public async Task<bool> SetActivePortraitAsync(
        long discordUserId, Guid uploadId, CancellationToken ct = default)
    {
        using var scope = m_ScopeFactory.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

        var entity = await context.UserPortraitUploads
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == uploadId && u.DiscordUserId == discordUserId &&
                                      !context.UserPortraitDeletions.Any(deletion => deletion.UserPortraitUploadId == u.Id), ct);

        if (entity == null)
            return false;

        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        await CharacterDbLock.AcquireAsync(context,
            $"portrait:{discordUserId}:{entity.Game}:{entity.CharacterName}", ct);

        entity = await context.UserPortraitUploads
            .FirstOrDefaultAsync(u => u.Id == uploadId && u.DiscordUserId == discordUserId &&
                                      !context.UserPortraitDeletions.Any(deletion => deletion.UserPortraitUploadId == u.Id), ct);
        if (entity == null)
            return false;

        // Deactivate all other portraits for the same character
        var siblings = await context.UserPortraitUploads
            .Where(u =>
                u.DiscordUserId == discordUserId &&
                u.Game == entity.Game &&
                u.CharacterName == entity.CharacterName &&
                u.Id != uploadId)
            .ToListAsync(ct);

        foreach (var sibling in siblings)
            sibling.IsActive = false;

        await context.SaveChangesAsync(ct);

        entity.IsActive = true;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        try
        {
            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        }
        catch (DbUpdateException e)
        {
            m_Logger.LogError(e, "Failed to set active portrait for {UploadId}", uploadId);
            return false;
        }
    }

    public async Task<bool> SetInactivePortraitAsync(
        long discordUserId, Guid uploadId, CancellationToken ct = default)
    {
        using var scope = m_ScopeFactory.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

        var entity = await context.UserPortraitUploads
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == uploadId && u.DiscordUserId == discordUserId &&
                                      !context.UserPortraitDeletions.Any(deletion => deletion.UserPortraitUploadId == u.Id), ct);

        if (entity == null)
            return false;

        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        await CharacterDbLock.AcquireAsync(context,
            $"portrait:{discordUserId}:{entity.Game}:{entity.CharacterName}", ct);

        entity = await context.UserPortraitUploads
            .FirstOrDefaultAsync(u => u.Id == uploadId && u.DiscordUserId == discordUserId &&
                                      !context.UserPortraitDeletions.Any(deletion => deletion.UserPortraitUploadId == u.Id), ct);
        if (entity == null)
            return false;

        entity.IsActive = false;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        try
        {
            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        }
        catch (DbUpdateException e)
        {
            m_Logger.LogError(e, "Failed to deactivate portrait {UploadId}", uploadId);
            return false;
        }
    }

    public async Task<bool> DeletePortraitAsync(long discordUserId, Guid uploadId, CancellationToken ct = default)
    {
        using var scope = m_ScopeFactory.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

        var entity = await context.UserPortraitUploads
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == uploadId && u.DiscordUserId == discordUserId &&
                                      !context.UserPortraitDeletions.Any(deletion => deletion.UserPortraitUploadId == u.Id), ct);

        if (entity == null)
            return false;

        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        await CharacterDbLock.AcquireAsync(context,
            $"portrait:{discordUserId}:{entity.Game}:{entity.CharacterName}", ct);

        entity = await context.UserPortraitUploads
            .FirstOrDefaultAsync(u => u.Id == uploadId && u.DiscordUserId == discordUserId &&
                                      !context.UserPortraitDeletions.Any(deletion => deletion.UserPortraitUploadId == u.Id), ct);
        if (entity == null)
            return false;

        // Commit the deletion intent before touching S3. The row is hidden from
        // reads while this durable outbox entry is pending.
        var deletion = new UserPortraitDeletionModel
        {
            UserPortraitUploadId = entity.Id,
            S3Key = entity.S3Key
        };
        context.UserPortraitDeletions.Add(deletion);

        try
        {
            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            // A failed storage call is intentionally recoverable; the hosted
            // processor will retry the same idempotent deletion.
            await m_DeletionProcessor.ProcessPendingDeletionAsync(deletion.Id, ct);
            return true;
        }
        catch (DbUpdateException e)
        {
            m_Logger.LogError(e, "Failed to delete portrait upload record {UploadId}", uploadId);
            return false;
        }
    }

    public async Task<int> GetUploadCountAsync(long discordUserId, Game game, string characterName, CancellationToken ct = default)
    {
        using var scope = m_ScopeFactory.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

        var normalized = characterName.ReplaceLineEndings("").Trim();

        return await context.UserPortraitUploads
            .CountAsync(u =>
                u.DiscordUserId == discordUserId &&
                u.Game == game &&
                u.CharacterName == normalized, ct);
    }

    private static UserPortraitUploadDto ToDto(UserPortraitUpload entity)
    {
        return new UserPortraitUploadDto
        {
            Id = entity.Id,
            DiscordUserId = entity.DiscordUserId,
            Game = entity.Game,
            CharacterName = entity.CharacterName,
            SHA256Hash = entity.SHA256Hash,
            S3Key = entity.S3Key,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt,
            Config = new UserPortraitConfigDto
            {
                OffsetX = entity.Config.OffsetX,
                OffsetY = entity.Config.OffsetY,
                TargetScale = entity.Config.TargetScale,
                FlipX = entity.Config.FlipX,
                ArtistAttribution = entity.Config.ArtistAttribution
            }
        };
    }

    private static string ResolveContentType(string s3Key)
    {
        var extension = Path.GetExtension(s3Key).ToLowerInvariant();
        return extension switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream"
        };
    }
}
