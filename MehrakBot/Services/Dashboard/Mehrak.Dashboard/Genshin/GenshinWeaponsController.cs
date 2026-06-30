using System.Text.RegularExpressions;
using Grpc.Core;
using Mehrak.Dashboard.Shared;
using Mehrak.Domain.Image;
using Mehrak.Domain.Protobuf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;

namespace Mehrak.Dashboard.Genshin;

[Authorize]
[Route("genshin/weapons")]
public partial class GenshinWeaponsController : GameWriteController
{
    private readonly IImageRepository m_ImageRepository;
    private readonly ImageProcessorService.ImageProcessorServiceClient m_ImageProcessorClient;
    private readonly ILogger<GenshinWeaponsController> m_Logger;

    private const string GenshinPrefix = "genshin/";
    private const string WeaponKeyPattern = @"^weapon_(base|ascended)_(\d+)\.png$";
    private const long MaxUploadBytes = 4 * 1024 * 1024;
    private const long MaxOverwriteBytes = 2 * 1024 * 1024;
    private const int ExpectedDimension = 200;
    private const string PngContentType = "image/png";

    public GenshinWeaponsController(
        IImageRepository imageRepository,
        ImageProcessorService.ImageProcessorServiceClient imageProcessorClient,
        ILogger<GenshinWeaponsController> logger)
    {
        m_ImageRepository = imageRepository;
        m_ImageProcessorClient = imageProcessorClient;
        m_Logger = logger;
    }

    [GeneratedRegex(WeaponKeyPattern)]
    private static partial Regex WeaponKeyRegex();

    private static (string Type, int Id)? ParseWeaponKey(string key)
    {
        var match = WeaponKeyRegex().Match(key);
        if (!match.Success) return null;
        if (!int.TryParse(match.Groups[2].Value, out var id)) return null;
        return (match.Groups[1].Value, id);
    }

    private static (int Width, int Height)? ReadPngDimensions(Stream stream)
    {
        var info = Image.Identify(stream);
        return info is null ? null : (info.Width, info.Height);
    }

    [HttpGet("list")]
    public async Task<IActionResult> ListWeapons([FromQuery] string? type, CancellationToken ct)
    {
        var files = await m_ImageRepository.ListFilesAsync("genshin/weapon_", ct);

        var weapons = files
            .Where(f => f.StartsWith(GenshinPrefix + "weapon_") && f.EndsWith(".png"))
            .Select(f => f[GenshinPrefix.Length..])
            .Select(key => ParseWeaponKey(key))
            .Where(parsed => parsed.HasValue)
            .Select(parsed => parsed!.Value)
            .GroupBy(p => p.Id)
            .Select(g => new
            {
                id = g.Key,
                hasBase = g.Any(p => p.Type == "base"),
                hasAscended = g.Any(p => p.Type == "ascended")
            })
            .Where(w => type switch
            {
                "base" => w.hasBase,
                "ascended" => w.hasAscended,
                _ => true
            })
            .OrderBy(w => w.id)
            .ToList();

        return Ok(new { weapons });
    }

    [HttpGet("icons/{key}")]
    public async Task<IActionResult> GetWeaponIcon(string key, CancellationToken ct)
    {
        if (ParseWeaponKey(key) is null)
            return BadRequest(new { error = "Invalid weapon icon key." });

        var s3Key = GenshinPrefix + key;

        try
        {
            var stream = await m_ImageRepository.DownloadFileToStreamAsync(s3Key, ct);
            Response.Headers.CacheControl = "public, max-age=86400";
            return File(stream, PngContentType);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return NotFound(new { error = "Image not found." });
        }
    }

    [HttpPost("process")]
    [Authorize(Policy = "RequireGameWrite")]
    [RequestSizeLimit(MaxUploadBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<IActionResult> ProcessWeaponImage(
        [FromForm] int weaponId, IFormFile ascendedImage, CancellationToken ct)
    {
        if (weaponId <= 0)
            return BadRequest(new { error = "Weapon ID must be a positive integer." });

        if (ascendedImage is null || ascendedImage.Length == 0)
            return BadRequest(new { error = "No file uploaded." });

        if (ascendedImage.Length > MaxUploadBytes)
            return BadRequest(new { error = "File size must be under 4MB." });

        if (ascendedImage.ContentType is null ||
            !ascendedImage.ContentType.StartsWith(PngContentType, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Only PNG images are allowed." });

        var baseKey = GenshinPrefix + $"weapon_base_{weaponId}.png";

        if (!await m_ImageRepository.FileExistsAsync(baseKey, ct))
            return NotFound(new { error = $"Base icon not found for weapon {weaponId}." });

        using var baseStream = await m_ImageRepository.DownloadFileToStreamAsync(baseKey, ct);

        var request = new ProcessWeaponImageRequest();
        request.Images.Add(Google.Protobuf.ByteString.FromStream(baseStream));

        using var ascendedStream = ascendedImage.OpenReadStream();
        request.Images.Add(Google.Protobuf.ByteString.FromStream(ascendedStream));

        ProcessWeaponImageResponse response;
        try
        {
            response = await m_ImageProcessorClient.ProcessWeaponImageAsync(request, cancellationToken: ct);
        }
        catch (RpcException ex)
        {
            m_Logger.LogError(ex, "gRPC error processing weapon image for weapon {WeaponId}", weaponId);
            return StatusCode(502, new { error = "Image processing service unavailable." });
        }

        if (response.ProcessedImage.IsEmpty)
            return StatusCode(422, new { error = "Image processing failed. The images could not be aligned." });

        return File(new MemoryStream(response.ProcessedImage.ToByteArray()), PngContentType);
    }

    [HttpPut("icons/{key}")]
    [Authorize(Policy = "RequireGameWrite")]
    [RequestSizeLimit(MaxOverwriteBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxOverwriteBytes)]
    public async Task<IActionResult> OverwriteWeaponIcon(string key, IFormFile image, CancellationToken ct)
    {
        if (ParseWeaponKey(key) is not { } parsed)
            return BadRequest(new { error = "Invalid weapon icon key." });

        if (parsed.Type != "ascended")
            return BadRequest(new { error = "Only ascended weapon icons can be overwritten." });

        if (image is null || image.Length == 0)
            return BadRequest(new { error = "No file uploaded." });

        if (image.ContentType is null ||
            !image.ContentType.StartsWith(PngContentType, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Only PNG images are allowed." });

        await using var stream = image.OpenReadStream();
        var dimensions = ReadPngDimensions(stream);
        if (dimensions is not { Width: var w, Height: var h })
            return BadRequest(new { error = "Could not read PNG dimensions." });

        if (w != ExpectedDimension || h != ExpectedDimension)
            return BadRequest(new { error = $"Image dimensions must be {ExpectedDimension}x{ExpectedDimension}. Got {w}x{h}." });

        var baseKey = GenshinPrefix + $"weapon_base_{parsed.Id}.png";
        if (!await m_ImageRepository.FileExistsAsync(baseKey, ct))
            return NotFound(new { error = $"Weapon base icon not found for weapon {parsed.Id}." });

        stream.Position = 0;
        var s3Key = GenshinPrefix + key;
        await m_ImageRepository.UploadFileAsync(s3Key, stream, PngContentType, ct);
        m_ImageRepository.InvalidateCache(s3Key);

        return Ok(new { key, size = image.Length });
    }
}
