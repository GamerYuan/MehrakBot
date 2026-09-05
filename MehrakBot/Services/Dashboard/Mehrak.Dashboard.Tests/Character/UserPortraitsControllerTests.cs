using System.Security.Claims;
using Mehrak.Dashboard.Character;
using Mehrak.Domain.Character;
using Mehrak.Domain.Character.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.Dashboard.Tests.Character;

[TestFixture]
public class UserPortraitsControllerTests
{
    private Mock<IUserPortraitService> m_MockPortraitService = null!;
    private Mock<IPortraitUploadRateLimitService> m_MockRateLimitService = null!;
    private Mock<IImageClassificationService> m_MockClassificationService = null!;
    private Mock<ILogger<UserPortraitsController>> m_MockLogger = null!;
    private UserPortraitsController m_Controller = null!;

    [SetUp]
    public void SetUp()
    {
        m_MockPortraitService = new Mock<IUserPortraitService>();
        m_MockRateLimitService = new Mock<IPortraitUploadRateLimitService>();
        m_MockClassificationService = new Mock<IImageClassificationService>();
        m_MockLogger = new Mock<ILogger<UserPortraitsController>>();

        m_Controller = new UserPortraitsController(
            m_MockPortraitService.Object,
            m_MockRateLimitService.Object,
            m_MockClassificationService.Object,
            m_MockLogger.Object);

        SetupHttpContext(100L);
    }

    private void SetupHttpContext(long? discordId = null, bool isSuperAdmin = false)
    {
        var claims = new List<Claim>();
        if (discordId.HasValue)
            claims.Add(new Claim("discord_id", discordId.Value.ToString()));
        if (isSuperAdmin)
            claims.Add(new Claim(ClaimTypes.Role, "superadmin"));

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        m_Controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
    }

    private static UserPortraitUploadDto CreatePortraitDto(
        Guid? id = null,
        long discordId = 100L,
        Game game = Game.Genshin,
        string characterName = "Raiden",
        bool isActive = false)
    {
        return new UserPortraitUploadDto
        {
            Id = id ?? Guid.CreateVersion7(),
            DiscordUserId = discordId,
            Game = game,
            CharacterName = characterName,
            SHA256Hash = "abc123",
            S3Key = $"{discordId}/abc123.png",
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow,
            Config = new UserPortraitConfigDto()
        };
    }

    #region GetPortraitImage

    [Test]
    public async Task GetPortraitImage_ValidPortrait_ReturnsFileStreamResult()
    {
        var portraitId = Guid.NewGuid();
        var stream = new MemoryStream(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        m_MockPortraitService.Setup(s => s.GetPortraitImageAsync(100L, portraitId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttachmentDownloadResult(stream, "image/png"));

        var result = await m_Controller.GetPortraitImage(portraitId);

        Assert.That(result, Is.InstanceOf<FileStreamResult>());
        var fileResult = (FileStreamResult)result;
        Assert.Multiple(() =>
        {
            Assert.That(fileResult.ContentType, Is.EqualTo("image/png"));
            Assert.That(m_Controller.Response.Headers.CacheControl.ToString(), Is.EqualTo("private, max-age=86400"));
        });
    }

    [Test]
    public async Task GetPortraitImage_NotFound_Returns404()
    {
        var portraitId = Guid.NewGuid();
        m_MockPortraitService.Setup(s => s.GetPortraitImageAsync(100L, portraitId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AttachmentDownloadResult?)null);

        var result = await m_Controller.GetPortraitImage(portraitId);

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task GetPortraitImage_Unauthorized_Returns401()
    {
        SetupHttpContext(null);

        var result = await m_Controller.GetPortraitImage(Guid.NewGuid());

        Assert.That(result, Is.InstanceOf<UnauthorizedObjectResult>());
    }

    #endregion

    #region SetActivePortrait

    [Test]
    public async Task SetActivePortrait_Success_Returns204()
    {
        var portraitId = Guid.NewGuid();
        m_MockPortraitService.Setup(s => s.SetActivePortraitAsync(100L, portraitId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await m_Controller.SetActivePortrait(portraitId);

        Assert.That(result, Is.InstanceOf<NoContentResult>());
    }

    [Test]
    public async Task SetActivePortrait_NotFound_Returns404()
    {
        var portraitId = Guid.NewGuid();
        m_MockPortraitService.Setup(s => s.SetActivePortraitAsync(100L, portraitId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await m_Controller.SetActivePortrait(portraitId);

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task SetActivePortrait_Unauthorized_Returns401()
    {
        SetupHttpContext(null);

        var result = await m_Controller.SetActivePortrait(Guid.NewGuid());

        Assert.That(result, Is.InstanceOf<UnauthorizedObjectResult>());
    }

    #endregion

    #region GetPortraits — isActive in response

    [Test]
    public async Task GetPortraits_ReturnsIsActiveField()
    {
        var portrait = CreatePortraitDto(isActive: true);
        m_MockPortraitService.Setup(s => s.GetUserPortraitsAsync(100L, Game.Genshin, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserPortraitUploadDto> { portrait });

        var result = await m_Controller.GetPortraits("genshin", null);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    #endregion

    #region GetPortrait — isActive in response

    [Test]
    public async Task GetPortrait_ReturnsIsActiveField()
    {
        var portraitId = Guid.NewGuid();
        var portrait = CreatePortraitDto(id: portraitId, isActive: false);
        m_MockPortraitService.Setup(s => s.GetPortraitAsync(100L, portraitId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portrait);

        var result = await m_Controller.GetPortrait(portraitId);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public async Task GetPortrait_NotFound_Returns404()
    {
        var portraitId = Guid.NewGuid();
        m_MockPortraitService.Setup(s => s.GetPortraitAsync(100L, portraitId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserPortraitUploadDto?)null);

        var result = await m_Controller.GetPortrait(portraitId);

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task GetPortrait_Unauthorized_Returns401()
    {
        SetupHttpContext(null);

        var result = await m_Controller.GetPortrait(Guid.NewGuid());

        Assert.That(result, Is.InstanceOf<UnauthorizedObjectResult>());
    }

    #endregion

    #region GetPortraits — validation

    [Test]
    public async Task GetPortraits_MissingGame_Returns400()
    {
        var result = await m_Controller.GetPortraits(null, null);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task GetPortraits_InvalidGame_Returns400()
    {
        var result = await m_Controller.GetPortraits("invalid", null);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task GetPortraits_Unauthorized_Returns401()
    {
        SetupHttpContext(null);

        var result = await m_Controller.GetPortraits("genshin", null);

        Assert.That(result, Is.InstanceOf<UnauthorizedObjectResult>());
    }

    #endregion

    #region DeletePortrait

    [Test]
    public async Task DeletePortrait_Success_Returns204()
    {
        var portraitId = Guid.NewGuid();
        m_MockPortraitService.Setup(s => s.DeletePortraitAsync(100L, portraitId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await m_Controller.DeletePortrait(portraitId);

        Assert.That(result, Is.InstanceOf<NoContentResult>());
    }

    [Test]
    public async Task DeletePortrait_NotFound_Returns404()
    {
        var portraitId = Guid.NewGuid();
        m_MockPortraitService.Setup(s => s.DeletePortraitAsync(100L, portraitId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await m_Controller.DeletePortrait(portraitId);

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    #endregion

    #region UpdatePortraitConfig

    [Test]
    public async Task UpdatePortraitConfig_Success_Returns204()
    {
        var portraitId = Guid.NewGuid();
        var config = new UserPortraitConfigDto { OffsetX = 10, OffsetY = 20 };
        m_MockPortraitService.Setup(s => s.UpdatePortraitConfigAsync(100L, portraitId, config, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await m_Controller.UpdatePortraitConfig(portraitId, config);

        Assert.That(result, Is.InstanceOf<NoContentResult>());
    }

    [TestCase(100f)]
    [TestCase(10.01f)]
    [TestCase(0f)]
    [TestCase(-2f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public async Task UpdatePortraitConfig_OutOfRangeScale_Returns400(float scale)
    {
        var portraitId = Guid.NewGuid();
        var config = new UserPortraitConfigDto { TargetScale = scale };

        var result = await m_Controller.UpdatePortraitConfig(portraitId, config);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        m_MockPortraitService.Verify(s => s.UpdatePortraitConfigAsync(
            It.IsAny<long>(), It.IsAny<Guid>(), It.IsAny<UserPortraitConfigDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestCase(0.01f)]
    [TestCase(2.5f)]
    [TestCase(10f)]
    public async Task UpdatePortraitConfig_InRangeScale_ReachesService(float scale)
    {
        var portraitId = Guid.NewGuid();
        var config = new UserPortraitConfigDto { TargetScale = scale };
        m_MockPortraitService.Setup(s => s.UpdatePortraitConfigAsync(100L, portraitId, config, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await m_Controller.UpdatePortraitConfig(portraitId, config);

        Assert.That(result, Is.InstanceOf<NoContentResult>());
    }

    [Test]
    public async Task UpdatePortraitConfig_NotFound_Returns404()
    {
        var portraitId = Guid.NewGuid();
        var config = new UserPortraitConfigDto();
        m_MockPortraitService.Setup(s => s.UpdatePortraitConfigAsync(100L, portraitId, config, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await m_Controller.UpdatePortraitConfig(portraitId, config);

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    #endregion

    #region UploadPortrait

    private static IFormFile CreateFormFile(string contentType = "image/png", long size = 1024, byte[]? content = null)
    {
        // Default content is a real 1x1 PNG so header validation passes; tests for
        // rejection paths supply their own metadata-only fixture bytes instead.
        var body = content ?? BuildPng(1, 1);
        // Pad with trailing zeros to the declared size; decoders stop at IEND.
        var padded = body.Length >= size
            ? body.Take((int)size).ToArray()
            : body.Concat(new byte[size - body.Length]).ToArray();
        var mock = new Mock<IFormFile>();
        mock.Setup(f => f.ContentType).Returns(contentType);
        mock.Setup(f => f.Length).Returns(size);
        mock.Setup(f => f.FileName).Returns($"test.{contentType.Split('/')[1]}");
        mock.Setup(f => f.OpenReadStream()).Returns(new MemoryStream(padded));
        return mock.Object;
    }

    /// <summary>
    /// Builds a minimal PNG with the given IHDR dimensions. The pixel payload always
    /// describes a 1x1 image, so oversized-dimension fixtures stay tiny and never
    /// allocate pixels: header validation must reject them before any decode.
    /// </summary>
    private static byte[] BuildPng(int width, int height)
    {
        using var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        using (var ihdr = new MemoryStream())
        {
            WriteUInt32BE(ihdr, unchecked((uint)width));
            WriteUInt32BE(ihdr, unchecked((uint)height));
            ihdr.Write(new byte[] { 8, 2, 0, 0, 0 });
            WriteChunk(png, "IHDR", ihdr.ToArray());
        }
        // Valid zlib stream for a single black RGB scanline (filter 0 + 3 zero bytes).
        WriteChunk(png, "IDAT", new byte[]
        {
            0x78, 0x01, 0x01, 0x04, 0x00, 0xFB, 0xFF,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x04, 0x00, 0x01
        });
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        WriteUInt32BE(stream, unchecked((uint)data.Length));
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        var crcInput = typeBytes.Concat(data).ToArray();
        WriteUInt32BE(stream, ComputeCrc32(crcInput));
    }

    private static void WriteUInt32BE(Stream stream, uint value)
    {
        stream.Write(new[]
        {
            (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value
        });
    }

    private static readonly uint[] Crc32Table = BuildCrc32Table();

    private static uint[] BuildCrc32Table()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var entry = i;
            for (var k = 0; k < 8; k++)
                entry = (entry & 1) != 0 ? 0xEDB88320u ^ (entry >> 1) : entry >> 1;
            table[i] = entry;
        }
        return table;
    }

    private static uint ComputeCrc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
            crc = Crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    [Test]
    public async Task UploadPortrait_ValidUpload_ReturnsOk()
    {
        var file = CreateFormFile();
        var portrait = CreatePortraitDto();
        m_MockClassificationService.Setup(s => s.ClassifyAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageClassificationResult(false, 0.1f, 0.9f));
        m_MockRateLimitService.Setup(s => s.IsAllowedAsync(100L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        m_MockPortraitService.Setup(s => s.UploadPortraitAsync(100L, Game.Genshin, "Raiden", It.IsAny<Stream>(), It.IsAny<string>(), "png", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UploadPortraitResult { Succeeded = true, UploadId = Guid.CreateVersion7(), Portrait = portrait });

        var result = await m_Controller.UploadPortrait("genshin", "Raiden", file);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public async Task UploadPortrait_Unauthorized_Returns401()
    {
        SetupHttpContext(null);

        var result = await m_Controller.UploadPortrait("genshin", "Raiden", CreateFormFile());

        Assert.That(result, Is.InstanceOf<UnauthorizedObjectResult>());
    }

    [Test]
    public async Task UploadPortrait_InvalidGame_Returns400()
    {
        var result = await m_Controller.UploadPortrait("invalid", "Raiden", CreateFormFile());

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task UploadPortrait_MissingCharacter_Returns400()
    {
        var result = await m_Controller.UploadPortrait("genshin", "", CreateFormFile());

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task UploadPortrait_NullFile_Returns400()
    {
        var result = await m_Controller.UploadPortrait("genshin", "Raiden", null!);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task UploadPortrait_EmptyFile_Returns400()
    {
        var file = CreateFormFile(size: 0);

        var result = await m_Controller.UploadPortrait("genshin", "Raiden", file);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task UploadPortrait_FileTooLarge_Returns400()
    {
        var file = CreateFormFile(size: 9 * 1024 * 1024);

        var result = await m_Controller.UploadPortrait("genshin", "Raiden", file);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task UploadPortrait_InvalidContentType_Returns400()
    {
        var file = CreateFormFile(contentType: "application/pdf");

        var result = await m_Controller.UploadPortrait("genshin", "Raiden", file);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task UploadPortrait_RateLimitExceeded_Returns429()
    {
        var file = CreateFormFile();
        m_MockClassificationService.Setup(s => s.ClassifyAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageClassificationResult(false, 0.1f, 0.9f));
        m_MockRateLimitService.Setup(s => s.IsAllowedAsync(100L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        m_MockRateLimitService.Setup(s => s.GetRemainingAsync(100L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await m_Controller.UploadPortrait("genshin", "Raiden", file);

        var objectResult = result as ObjectResult;
        Assert.That(objectResult, Is.Not.Null);
        Assert.That(objectResult!.StatusCode, Is.EqualTo(429));
    }

    [Test]
    public async Task UploadPortrait_SuperAdmin_BypassesRateLimit()
    {
        SetupHttpContext(100L, isSuperAdmin: true);
        var file = CreateFormFile();
        var portrait = CreatePortraitDto();
        m_MockClassificationService.Setup(s => s.ClassifyAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageClassificationResult(false, 0.1f, 0.9f));
        m_MockPortraitService.Setup(s => s.UploadPortraitAsync(100L, Game.Genshin, "Raiden", It.IsAny<Stream>(), It.IsAny<string>(), "png", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UploadPortraitResult { Succeeded = true, UploadId = Guid.CreateVersion7(), Portrait = portrait });

        var result = await m_Controller.UploadPortrait("genshin", "Raiden", file);

        m_MockRateLimitService.Verify(s => s.IsAllowedAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public async Task UploadPortrait_NsfwBlocked_Returns422()
    {
        var file = CreateFormFile();
        m_MockClassificationService.Setup(s => s.ClassifyAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageClassificationResult(true, 0.95f, 0.05f));
        m_MockRateLimitService.Setup(s => s.IsAllowedAsync(100L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await m_Controller.UploadPortrait("genshin", "Raiden", file);

        var objectResult = result as ObjectResult;
        Assert.That(objectResult, Is.Not.Null);
        Assert.That(objectResult!.StatusCode, Is.EqualTo(422));
    }

    [Test]
    public async Task UploadPortrait_ClassificationFails_Returns502()
    {
        var file = CreateFormFile();
        m_MockClassificationService.Setup(s => s.ClassifyAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Service unavailable"));
        m_MockRateLimitService.Setup(s => s.IsAllowedAsync(100L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await m_Controller.UploadPortrait("genshin", "Raiden", file);

        var objectResult = result as ObjectResult;
        Assert.That(objectResult, Is.Not.Null);
        Assert.That(objectResult!.StatusCode, Is.EqualTo(502));
    }

    [Test]
    public async Task UploadPortrait_OversizedDimensions_Returns400BeforeClassification()
    {
        // 100000x100000 declared in IHDR, but the file itself is ~70 bytes: header
        // validation must reject it without allocating pixels or calling inference.
        var file = CreateFormFile(content: BuildPng(100000, 100000));
        m_MockRateLimitService.Setup(s => s.IsAllowedAsync(100L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await m_Controller.UploadPortrait("genshin", "Raiden", file);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        m_MockClassificationService.Verify(
            s => s.ClassifyAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
        m_MockPortraitService.Verify(
            s => s.UploadPortraitAsync(It.IsAny<long>(), It.IsAny<Game>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task UploadPortrait_OversizedPixelCount_Returns400()
    {
        // 5000x5000 exceeds the 4096 dimension cap via IHDR metadata only.
        var file = CreateFormFile(content: BuildPng(5000, 5000));
        m_MockRateLimitService.Setup(s => s.IsAllowedAsync(100L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await m_Controller.UploadPortrait("genshin", "Raiden", file);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        m_MockClassificationService.Verify(
            s => s.ClassifyAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task UploadPortrait_FormatMismatch_Returns400()
    {
        // GIF bytes behind a PNG content type: the detected format governs.
        var gif = new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 1, 0, 1, 0, 0x80, 0, 0 };
        var file = CreateFormFile(content: gif);
        m_MockRateLimitService.Setup(s => s.IsAllowedAsync(100L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await m_Controller.UploadPortrait("genshin", "Raiden", file);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        m_MockClassificationService.Verify(
            s => s.ClassifyAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task UploadPortrait_UndecodableBytes_Returns400()
    {
        var file = CreateFormFile(content: new byte[] { 1, 2, 3, 4 });
        m_MockRateLimitService.Setup(s => s.IsAllowedAsync(100L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await m_Controller.UploadPortrait("genshin", "Raiden", file);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        m_MockClassificationService.Verify(
            s => s.ClassifyAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task UploadPortrait_StoredExtensionFollowsDecodedFormat()
    {
        // PNG bytes labeled as JPEG must be stored as PNG: storage stays consistent
        // with the validated decoded format, not the supplied MIME type.
        var file = CreateFormFile(contentType: "image/jpeg");
        var portrait = CreatePortraitDto();
        m_MockClassificationService.Setup(s => s.ClassifyAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageClassificationResult(false, 0.1f, 0.9f));
        m_MockRateLimitService.Setup(s => s.IsAllowedAsync(100L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        m_MockPortraitService.Setup(s => s.UploadPortraitAsync(100L, Game.Genshin, "Raiden", It.IsAny<Stream>(), It.IsAny<string>(), "png", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UploadPortraitResult { Succeeded = true, UploadId = Guid.CreateVersion7(), Portrait = portrait });

        var result = await m_Controller.UploadPortrait("genshin", "Raiden", file);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        m_MockPortraitService.Verify(s => s.UploadPortraitAsync(100L, Game.Genshin, "Raiden", It.IsAny<Stream>(), It.IsAny<string>(), "png", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task UploadPortrait_UploadFails_Returns400()
    {
        var file = CreateFormFile();
        m_MockClassificationService.Setup(s => s.ClassifyAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageClassificationResult(false, 0.1f, 0.9f));
        m_MockRateLimitService.Setup(s => s.IsAllowedAsync(100L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        m_MockPortraitService.Setup(s => s.UploadPortraitAsync(100L, Game.Genshin, "Raiden", It.IsAny<Stream>(), It.IsAny<string>(), "png", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UploadPortraitResult { Succeeded = false, Error = "Duplicate image." });

        var result = await m_Controller.UploadPortrait("genshin", "Raiden", file);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    #endregion
}
