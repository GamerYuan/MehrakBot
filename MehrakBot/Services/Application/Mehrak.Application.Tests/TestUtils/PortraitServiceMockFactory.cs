#region

using Mehrak.Domain.Character;
using Mehrak.Domain.Character.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Services;
using Moq;

#endregion

namespace Mehrak.Application.Tests.TestUtils;

/// <summary>
/// Helpers for creating <see cref="IUserPortraitService"/> mocks in card tests.
/// </summary>
public static class PortraitServiceMockFactory
{
    /// <summary>
    /// A portrait service that reports no active portrait, so the card
    /// service takes the stock-image path (preserving existing golden images).
    /// </summary>
    public static IUserPortraitService CreateEmpty()
    {
        var mock = new Mock<IUserPortraitService>();
        mock.Setup(x => x.GetUserPortraitsAsync(It.IsAny<long>(), It.IsAny<Game>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<UserPortraitUploadDto>());
        return mock.Object;
    }

    /// <summary>
    /// A portrait service that reports a single active user portrait, downloading
    /// <paramref name="imageStream" /> as the portrait image bytes. The returned
    /// mock is pre-configured and ready for <see cref="Mock{T}.Verify(System.Linq.Expressions.Expression{Action{T}}>, Times)"/>
    /// assertions, so callers can confirm which branch the card service took.
    /// </summary>
    public static Mock<IUserPortraitService> CreateWithActivePortrait(
        Guid uploadId, Stream imageStream, string contentType = "image/png")
    {
        var mock = new Mock<IUserPortraitService>();
        mock.Setup(x => x.GetUserPortraitsAsync(It.IsAny<long>(), It.IsAny<Game>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long _, Game _, string? _, CancellationToken _) =>
                new[]
                {
                    new UserPortraitUploadDto
                    {
                        Id = uploadId,
                        IsActive = true,
                        Config = new UserPortraitConfigDto()
                    }
                });

        mock.Setup(x => x.GetPortraitImageAsync(It.IsAny<long>(), uploadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AttachmentDownloadResult(imageStream, contentType));

        return mock;
    }

    /// <summary>
    /// A portrait service that reports a single active user portrait, but returns
    /// null from <see cref="IUserPortraitService.GetPortraitImageAsync"/> to simulate
    /// a failed download. The card service should fall back to the stock portrait.
    /// </summary>
    public static Mock<IUserPortraitService> CreateWithFailingDownload(Guid uploadId)
    {
        var mock = new Mock<IUserPortraitService>();
        mock.Setup(x => x.GetUserPortraitsAsync(It.IsAny<long>(), It.IsAny<Game>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long _, Game _, string? _, CancellationToken _) =>
                new[]
                {
                    new UserPortraitUploadDto
                    {
                        Id = uploadId,
                        IsActive = true,
                        Config = new UserPortraitConfigDto()
                    }
                });
        mock.Setup(x => x.GetPortraitImageAsync(It.IsAny<long>(), uploadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AttachmentDownloadResult?)null);
        return mock;
    }

    /// <summary>
    /// Creates a PNG stream of the given size filled with a solid RGB color, for use as a
    /// stand-in portrait image in tests.
    /// </summary>
    public static MemoryStream CreateSolidColorPngStream(int width, int height, (byte R, byte G, byte B) color)
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgb24>(width, height,
            new SixLabors.ImageSharp.PixelFormats.Rgb24(color.R, color.G, color.B));
        var ms = new MemoryStream();
        image.Save(ms, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        ms.Position = 0;
        return ms;
    }
}
