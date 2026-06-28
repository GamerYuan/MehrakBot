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
        catch (Exception e)
        {
            m_Logger.LogError(e, "Failed to retrieve portrait image from S3: {S3Key}", entity.S3Key);
            return null;
        }
    }

    public async Task<AttachmentDownloadResult?> GetPortraitImageAsync(
        long discordUserId, string s3Key, Guid uploadId, CancellationToken ct = default)
    {
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

        var response = await m_S3.PutObjectAsync(putReq, ct);
        if ((int)response.HttpStatusCode >= 300)
        {
            m_Logger.LogError("Failed to upload portrait to S3. Status: {StatusCode}", response.HttpStatusCode);
            return new UploadPortraitResult
            {
                Succeeded = false,
                Error = "Failed to upload image to storage."
            };
        }

        // Create DB record
        var isFirstForCharacter = existingCount == 0;

        var upload = new UserPortraitUpload
        {
            Id = uploadId,
            DiscordUserId = discordUserId,
            Game = game,
            CharacterName = normalizedCharacter,
            SHA256Hash = sha256,
            S3Key = s3Key,
            IsActive = isFirstForCharacter,
            Config = new UserPortraitConfigModel
            {
                Id = Guid.NewGuid()
            }
        };

        context.UserPortraitUploads.Add(upload);

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e)
        {
            m_Logger.LogError(e, "Failed to save portrait upload record");
            try
            {
                await m_S3.DeleteObjectAsync(new DeleteObjectRequest
                {
                    BucketName = m_Bucket,
                    Key = s3Key
                }, ct);
            }
            catch (Exception cleanupEx)
            {
                m_Logger.LogWarning(cleanupEx, "Failed to clean up orphaned S3 object: {S3Key}", s3Key);
            }
            return new UploadPortraitResult
            {
                Succeeded = false,
                Error = "Failed to save upload record."
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

        entity.IsActive = true;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        try
        {
            await context.SaveChangesAsync(ct);
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
            .FirstOrDefaultAsync(u => u.Id == uploadId && u.DiscordUserId == discordUserId, ct);

        if (entity == null)
            return false;

        entity.IsActive = false;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        try
        {
            await context.SaveChangesAsync(ct);
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
            .FirstOrDefaultAsync(u => u.Id == uploadId && u.DiscordUserId == discordUserId, ct);

        if (entity == null)
            return false;

        // Delete from S3
        try
        {
            await m_S3.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = m_Bucket,
                Key = entity.S3Key
            }, ct);
        }
        catch (Exception e)
        {
            m_Logger.LogWarning(e, "Failed to delete portrait from S3: {S3Key}", entity.S3Key);
        }

        context.UserPortraitUploads.Remove(entity);

        try
        {
            await context.SaveChangesAsync(ct);
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
