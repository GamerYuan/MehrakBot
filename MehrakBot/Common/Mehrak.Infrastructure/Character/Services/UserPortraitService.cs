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
using Microsoft.Extensions.Options;

namespace Mehrak.Infrastructure.Character.Services;

internal class UserPortraitService : IUserPortraitService
{
    private const int MaxPortraitsPerCharacter = 5;

    private readonly IServiceScopeFactory m_ScopeFactory;
    private readonly IAmazonS3 m_S3;
    private readonly string m_Bucket;
    private readonly ILogger<UserPortraitService> m_Logger;

    public UserPortraitService(
        IServiceScopeFactory scopeFactory,
        IAmazonS3 s3,
        IOptions<UserPortraitStorageConfig> options,
        ILogger<UserPortraitService> logger)
    {
        m_ScopeFactory = scopeFactory;
        m_S3 = s3;
        m_Bucket = options.Value.Bucket;
        m_Logger = logger;
    }

    public async Task<IReadOnlyCollection<UserPortraitUploadDto>> GetUserPortraitsAsync(
        long discordUserId, Game game, string? characterName, CancellationToken ct = default)
    {
        using var scope = m_ScopeFactory.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

        var query = context.UserPortraitUploads
            .AsNoTracking()
            .Include(u => u.Config)
            .Where(u => u.DiscordUserId == discordUserId && u.Game == game);

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
            .FirstOrDefaultAsync(u => u.Id == uploadId && u.DiscordUserId == discordUserId, ct);

        return entity == null ? null : ToDto(entity);
    }

    public async Task<AttachmentDownloadResult?> GetPortraitImageAsync(
        long discordUserId, Guid uploadId, CancellationToken ct = default)
    {
        using var scope = m_ScopeFactory.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

        var entity = await context.UserPortraitUploads
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == uploadId && u.DiscordUserId == discordUserId, ct);

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
            .AnyAsync(u => u.Id == uploadId && u.DiscordUserId == discordUserId && u.S3Key == s3Key, ct);

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

        if (existingCount >= MaxPortraitsPerCharacter)
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

        // Upload to S3
        var uploadId = Guid.CreateVersion7();
        var s3Key = $"{discordUserId}/{uploadId}.{extension}";
        var contentType = extension == "png" ? "image/png" : "image/jpeg";

        if (imageStream.CanSeek) imageStream.Position = 0;

        var putReq = new PutObjectRequest
        {
            BucketName = m_Bucket,
            Key = s3Key,
            InputStream = imageStream,
            AutoCloseStream = false,
            ContentType = contentType
        };

        UserPortraitUpload? upload;
        try
        {
            var response = await m_S3.PutObjectAsync(putReq, ct);
            if ((int)response.HttpStatusCode >= 300)
                throw new AmazonS3Exception($"S3 returned status {response.HttpStatusCode}.");

            upload = new UserPortraitUpload
            {
                Id = uploadId,
                DiscordUserId = discordUserId,
                Game = game,
                CharacterName = normalizedCharacter,
                SHA256Hash = sha256,
                S3Key = s3Key,
                IsActive = existingCount == 0,
                Config = new UserPortraitConfigModel { Id = Guid.NewGuid() }
            };
            context.UserPortraitUploads.Add(upload);
            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception exception)
        {
            // Release the transaction before checking a possibly committed upload.
            await transaction.DisposeAsync();
            m_Logger.LogWarning(exception, "Portrait upload failed for {S3Key}", s3Key);
            upload = await CheckFailedUploadAsync(discordUserId, game, normalizedCharacter, s3Key);
            if (upload == null)
            {
                if (exception is OperationCanceledException && ct.IsCancellationRequested)
                    throw;

                return new UploadPortraitResult
                {
                    Succeeded = false,
                    Error = "Failed to upload portrait. Please try again."
                };
            }
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

    private async Task<UserPortraitUpload?> CheckFailedUploadAsync(
        long discordUserId, Game game, string characterName, string s3Key)
    {
        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var ct = cleanupTimeout.Token;
        try
        {
            using var scope = m_ScopeFactory.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            await CharacterDbLock.AcquireAsync(context, $"portrait:{discordUserId}:{game}:{characterName}", ct);
            var committed = await context.UserPortraitUploads.Include(upload => upload.Config)
                .SingleOrDefaultAsync(upload => upload.S3Key == s3Key, ct);

            // A lost commit acknowledgement must never cause deletion of a saved portrait.
            if (committed != null)
                return committed;

            context.UserPortraitDeletions.Add(new UserPortraitDeletionModel { S3Key = s3Key });
            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception exception)
        {
            // Best effort only: a crash or database outage can leave an orphaned object.
            // No persistent reservation prevents the user from retrying their upload.
            m_Logger.LogWarning(exception, "Could not queue failed portrait upload cleanup for {S3Key}", s3Key);
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
            .FirstOrDefaultAsync(u => u.Id == uploadId && u.DiscordUserId == discordUserId, ct);

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
            .FirstOrDefaultAsync(u => u.Id == uploadId && u.DiscordUserId == discordUserId, ct);

        if (entity == null)
            return false;

        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        await CharacterDbLock.AcquireAsync(context,
            $"portrait:{discordUserId}:{entity.Game}:{entity.CharacterName}", ct);

        entity = await context.UserPortraitUploads
            .FirstOrDefaultAsync(u => u.Id == uploadId && u.DiscordUserId == discordUserId, ct);
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
            .FirstOrDefaultAsync(u => u.Id == uploadId && u.DiscordUserId == discordUserId, ct);

        if (entity == null)
            return false;

        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        await CharacterDbLock.AcquireAsync(context,
            $"portrait:{discordUserId}:{entity.Game}:{entity.CharacterName}", ct);

        entity = await context.UserPortraitUploads
            .FirstOrDefaultAsync(u => u.Id == uploadId && u.DiscordUserId == discordUserId, ct);
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
            .FirstOrDefaultAsync(u => u.Id == uploadId && u.DiscordUserId == discordUserId, ct);

        if (entity == null)
            return false;

        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        await CharacterDbLock.AcquireAsync(context,
            $"portrait:{discordUserId}:{entity.Game}:{entity.CharacterName}", ct);

        entity = await context.UserPortraitUploads
            .FirstOrDefaultAsync(u => u.Id == uploadId && u.DiscordUserId == discordUserId, ct);
        if (entity == null)
            return false;

        // Free quota and remove the portrait atomically with queuing its storage key.
        // Retried storage deletion must never affect a replacement upload.
        var deletion = new UserPortraitDeletionModel
        {
            UserPortraitUploadId = entity.Id,
            S3Key = entity.S3Key
        };
        context.UserPortraitUploads.Remove(entity);
        context.UserPortraitDeletions.Add(deletion);

        try
        {
            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            // The hosted processor retries storage deletion independently of the request.
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
