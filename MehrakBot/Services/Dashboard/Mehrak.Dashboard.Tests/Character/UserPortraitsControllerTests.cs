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

    private static IFormFile CreateFormFile(string contentType = "image/png", long size = 1024)
    {
        var mock = new Mock<IFormFile>();
        mock.Setup(f => f.ContentType).Returns(contentType);
        mock.Setup(f => f.Length).Returns(size);
        mock.Setup(f => f.FileName).Returns($"test.{contentType.Split('/')[1]}");
        mock.Setup(f => f.OpenReadStream()).Returns(new MemoryStream(new byte[size]));
        return mock.Object;
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
