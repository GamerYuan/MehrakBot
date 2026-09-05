using System.Security.Claims;
using System.Security.Cryptography;
using Mehrak.Domain.Character;
using Mehrak.Domain.Character.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;

namespace Mehrak.Dashboard.Character;

[ApiController]
[Authorize]
[Route("user-portraits")]
public class UserPortraitsController : ControllerBase
{
    private const long MaxFileSizeBytes = 8 * 1024 * 1024; // 8 MB
    private const string PngContentType = "image/png";
    private const string JpgContentType = "image/jpeg";

    // Decoded-pixel budget: a small compressed payload can still describe a huge
    // image, so geometry is validated from container headers before any native decode.
    private const int MaxImageWidth = 4096;
    private const int MaxImageHeight = 4096;
    private const long MaxImagePixels = 16_777_216; // 4096 x 4096
    private const int MaxImageFrames = 1;

    private readonly IUserPortraitService m_PortraitService;
    private readonly IPortraitUploadRateLimitService m_RateLimitService;
    private readonly IImageClassificationService m_ClassificationService;
    private readonly ILogger<UserPortraitsController> m_Logger;

    public UserPortraitsController(
        IUserPortraitService portraitService,
        IPortraitUploadRateLimitService rateLimitService,
        IImageClassificationService classificationService,
        ILogger<UserPortraitsController> logger)
    {
        m_PortraitService = portraitService;
        m_RateLimitService = rateLimitService;
        m_ClassificationService = classificationService;
        m_Logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetPortraits([FromQuery] string? game, [FromQuery] string? character)
    {
        var discordUserId = GetDiscordUserId();
        if (discordUserId == null)
            return Unauthorized(new { error = "Invalid user identity." });

        if (string.IsNullOrWhiteSpace(game))
            return BadRequest(new { error = "Game parameter is required." });

        if (!Enum.TryParse<Game>(game, true, out var gameEnum) || !Enum.IsDefined(gameEnum))
            return BadRequest(new { error = "Invalid game parameter." });

        var portraits = await m_PortraitService.GetUserPortraitsAsync(discordUserId.Value, gameEnum, character, HttpContext.RequestAborted);

        return Ok(portraits.Select(p => new
        {
            id = p.Id,
            game = p.Game.ToString(),
            characterName = p.CharacterName,
            sha256Hash = p.SHA256Hash,
            isActive = p.IsActive,
            config = p.Config,
            createdAt = p.CreatedAt
        }));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetPortrait(Guid id)
    {
        var discordUserId = GetDiscordUserId();
        if (discordUserId == null)
            return Unauthorized(new { error = "Invalid user identity." });

        var portrait = await m_PortraitService.GetPortraitAsync(discordUserId.Value, id, HttpContext.RequestAborted);
        if (portrait == null)
            return NotFound(new { error = "Portrait not found." });

        return Ok(new
        {
            id = portrait.Id,
            game = portrait.Game.ToString(),
            characterName = portrait.CharacterName,
            sha256Hash = portrait.SHA256Hash,
            isActive = portrait.IsActive,
            config = portrait.Config,
            createdAt = portrait.CreatedAt
        });
    }

    [HttpGet("{id:guid}/image")]
    public async Task<IActionResult> GetPortraitImage(Guid id)
    {
        var discordUserId = GetDiscordUserId();
        if (discordUserId == null)
            return Unauthorized(new { error = "Invalid user identity." });

        var result = await m_PortraitService.GetPortraitImageAsync(discordUserId.Value, id, HttpContext.RequestAborted);
        if (result == null)
            return NotFound(new { error = "Portrait image not found." });

        Response.Headers.CacheControl = "private, max-age=86400";
        return File(result.Content, result.ContentType);
    }

    [HttpPost("upload")]
    [RequestSizeLimit(MaxFileSizeBytes + 1024 * 1024)] // 9 MB Kestrel limit, form reader enforces the 8 MB file limit
    [RequestFormLimits(MultipartBodyLengthLimit = MaxFileSizeBytes)]
    public async Task<IActionResult> UploadPortrait([FromQuery] string game, [FromQuery] string character, IFormFile file)
    {
        var discordUserId = GetDiscordUserId();
        if (discordUserId == null)
            return Unauthorized(new { error = "Invalid user identity." });

        if (string.IsNullOrWhiteSpace(game) || !Enum.TryParse<Game>(game, true, out var gameEnum))
            return BadRequest(new { error = "Invalid game parameter." });

        if (string.IsNullOrWhiteSpace(character))
            return BadRequest(new { error = "Character parameter is required." });

        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded." });

        if (file.Length > MaxFileSizeBytes)
            return BadRequest(new { error = $"File size must be under {MaxFileSizeBytes / 1024 / 1024}MB." });

        if (!file.ContentType.Equals(PngContentType, StringComparison.OrdinalIgnoreCase) &&
            !file.ContentType.Equals(JpgContentType, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Only PNG and JPG images are allowed." });

        // Rate limit check
        if (!HttpContext.User.IsInRole("superadmin") && !await m_RateLimitService.IsAllowedAsync(discordUserId.Value, HttpContext.RequestAborted))
        {
            var remaining = await m_RateLimitService.GetRemainingAsync(discordUserId.Value, HttpContext.RequestAborted);
            return StatusCode(429, new { error = "Upload rate limit exceeded. Try again later.", remaining });
        }

        // Buffer the upload so header validation, hashing, classification, and storage
        // all operate on the same bytes without re-reading the client stream.
        byte[] fileBytes;
        using (var buffer = new MemoryStream())
        {
            await using var uploadStream = file.OpenReadStream();
            await uploadStream.CopyToAsync(buffer, HttpContext.RequestAborted);
            fileBytes = buffer.ToArray();
        }

        // Validate actual image geometry from container headers before any native decode.
        ImageInfo? imageInfo;
        try
        {
            using var identifyStream = new MemoryStream(fileBytes, writable: false);
            imageInfo = Image.Identify(identifyStream);
        }
        catch (Exception ex)
        {
            m_Logger.LogWarning(ex, "Rejected portrait upload with undecodable image for user {DiscordUserId}", discordUserId);
            return BadRequest(new { error = "File is not a valid PNG or JPG image." });
        }

        var imageFormat = imageInfo?.Metadata.DecodedImageFormat;
        if (imageInfo is null || imageFormat is null || !IsSupportedUploadFormat(imageFormat))
            return BadRequest(new { error = "Only PNG and JPG images are allowed." });

        if (imageInfo.Width > MaxImageWidth || imageInfo.Height > MaxImageHeight ||
            (long)imageInfo.Width * imageInfo.Height > MaxImagePixels)
            return BadRequest(new { error = $"Image dimensions exceed the {MaxImageWidth}x{MaxImageHeight} ({MaxImagePixels} pixel) limit." });

        if (imageInfo.FrameCount > MaxImageFrames)
            return BadRequest(new { error = "Animated images are not allowed." });

        // Store under the actual decoded format so the stored object stays consistent
        // with what was validated, regardless of the supplied MIME type.
        var extension = imageFormat.DefaultMimeType.Equals(PngContentType, StringComparison.OrdinalIgnoreCase) ? "png" : "jpg";

        var sha256 = Convert.ToHexString(SHA256.HashData(fileBytes)).ToLowerInvariant();

        // NSFW classification
        try
        {
            using var classificationStream = new MemoryStream(fileBytes, writable: false);
            var classification = await m_ClassificationService.ClassifyAsync(classificationStream, HttpContext.RequestAborted);
            if (classification.IsNsfw)
            {
                m_Logger.LogInformation("NSFW image upload blocked for user {DiscordUserId}: confidence={Confidence:F4}",
                    discordUserId, classification.NsfwConfidence);
                return UnprocessableEntity(new
                {
                    error = "Image was classified as NSFW and cannot be uploaded."
                });
            }
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Failed to classify image for user {DiscordUserId}", discordUserId);
            return StatusCode(502, new { error = "Image classification service unavailable." });
        }

        // Upload to S3 and save to DB
        using var portraitStream = new MemoryStream(fileBytes, writable: false);
        var result = await m_PortraitService.UploadPortraitAsync(
            discordUserId.Value, gameEnum, character, portraitStream, sha256, extension, HttpContext.RequestAborted);

        if (!result.Succeeded)
            return BadRequest(new { error = result.Error });

        m_Logger.LogInformation("Portrait uploaded by user {DiscordUserId}: {UploadId}",
            discordUserId, result.UploadId);

        return Ok(new
        {
            id = result.UploadId,
            portrait = result.Portrait != null ? new
            {
                id = result.Portrait.Id,
                game = result.Portrait.Game.ToString(),
                characterName = result.Portrait.CharacterName,
                sha256Hash = result.Portrait.SHA256Hash,
                isActive = result.Portrait.IsActive,
                config = result.Portrait.Config,
                createdAt = result.Portrait.CreatedAt
            } : null
        });
    }

    [HttpPatch("{id:guid}/config")]
    public async Task<IActionResult> UpdatePortraitConfig(Guid id, [FromBody] UserPortraitConfigDto config)
    {
        var discordUserId = GetDiscordUserId();
        if (discordUserId == null)
            return Unauthorized(new { error = "Invalid user identity." });

        var success = await m_PortraitService.UpdatePortraitConfigAsync(discordUserId.Value, id, config, HttpContext.RequestAborted);
        if (!success)
            return NotFound(new { error = "Portrait not found." });

        return NoContent();
    }

    [HttpPatch("{id:guid}/active")]
    public async Task<IActionResult> SetActivePortrait(Guid id)
    {
        var discordUserId = GetDiscordUserId();
        if (discordUserId == null)
            return Unauthorized(new { error = "Invalid user identity." });

        var success = await m_PortraitService.SetActivePortraitAsync(discordUserId.Value, id, HttpContext.RequestAborted);
        if (!success)
            return NotFound(new { error = "Portrait not found." });

        return NoContent();
    }

    [HttpPatch("{id:guid}/inactive")]
    public async Task<IActionResult> SetInactivePortrait(Guid id)
    {
        var discordUserId = GetDiscordUserId();
        if (discordUserId == null)
            return Unauthorized(new { error = "Invalid user identity." });

        var success = await m_PortraitService.SetInactivePortraitAsync(discordUserId.Value, id, HttpContext.RequestAborted);
        if (!success)
            return NotFound(new { error = "Portrait not found." });

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeletePortrait(Guid id)
    {
        var discordUserId = GetDiscordUserId();
        if (discordUserId == null)
            return Unauthorized(new { error = "Invalid user identity." });

        var success = await m_PortraitService.DeletePortraitAsync(discordUserId.Value, id, HttpContext.RequestAborted);
        if (!success)
            return NotFound(new { error = "Portrait not found." });

        return NoContent();
    }

    private static bool IsSupportedUploadFormat(IImageFormat format) =>
        format.DefaultMimeType.Equals(PngContentType, StringComparison.OrdinalIgnoreCase) ||
        format.DefaultMimeType.Equals(JpgContentType, StringComparison.OrdinalIgnoreCase);

    private long? GetDiscordUserId()
    {
        var claim = User.FindFirstValue("discord_id");
        if (string.IsNullOrWhiteSpace(claim) || !long.TryParse(claim, out var discordUserId))
            return null;
        return discordUserId;
    }
}
