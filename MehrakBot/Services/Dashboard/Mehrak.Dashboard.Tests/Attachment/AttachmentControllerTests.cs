using Mehrak.Dashboard.Attachment;
using Mehrak.Dashboard.Shared.Models;
using Mehrak.Dashboard.Shared.Services;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Mehrak.Dashboard.Tests.Attachment;

[TestFixture]
public class AttachmentControllerTests
{
    [Test]
    public async Task AvailableCard_StorageFileNameStillDownloadsFromExistingAuthorizedRoute()
    {
        var key = new string('a', 64) + ".png";
        var response = (ObjectResult)DashboardApplicationExecutionResult.FromCommandResult(CommandResult.Success(
            EndgameOutcome.Card(Game.Genshin, "123456789", EndgameMode.SpiralAbyss, StorageReference.Attachment(key)))).MapToActionResult();
        var dto = (DashboardEndgameDto)response.Value!;
        var storage = new Mock<IAttachmentStorageService>();
        using var content = new MemoryStream([1, 2, 3]);
        storage.Setup(service => service.IsValidStorageFileName(key)).Returns(true);
        storage.Setup(service => service.DownloadAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttachmentDownloadResult(content, "image/png"));
        var controller = new AttachmentController(storage.Object, NullLogger<AttachmentController>.Instance);

        var result = (FileStreamResult)await controller.Download(dto.StorageFileName!, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(result.FileStream, Is.SameAs(content));
            Assert.That(result.ContentType, Is.EqualTo("image/png"));
            Assert.That(typeof(AttachmentController).GetCustomAttributes(typeof(AuthorizeAttribute), true), Has.Length.EqualTo(1));
            Assert.That(((RouteAttribute)typeof(AttachmentController).GetCustomAttributes(typeof(RouteAttribute), true).Single()).Template, Is.EqualTo("attachments"));
            Assert.That(((HttpGetAttribute)typeof(AttachmentController).GetMethod(nameof(AttachmentController.Download))!
                .GetCustomAttributes(typeof(HttpGetAttribute), true).Single()).Template, Is.EqualTo("{fileName}"));
        });
        storage.Verify(service => service.DownloadAsync(key, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task InvalidIdentifier_RemainsBadRequestWithoutDownload()
    {
        var storage = new Mock<IAttachmentStorageService>();
        var controller = new AttachmentController(storage.Object, NullLogger<AttachmentController>.Instance);
        var result = await controller.Download("../invalid", CancellationToken.None);
        Assert.That(result, Is.TypeOf<BadRequestObjectResult>());
        storage.Verify(service => service.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task MissingIdentifier_RemainsNotFound()
    {
        var storage = new Mock<IAttachmentStorageService>();
        storage.Setup(service => service.IsValidStorageFileName("missing.png")).Returns(true);
        var controller = new AttachmentController(storage.Object, NullLogger<AttachmentController>.Instance);
        var result = await controller.Download("missing.png", CancellationToken.None);
        Assert.That(result, Is.TypeOf<NotFoundObjectResult>());
    }
}
