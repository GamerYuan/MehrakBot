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
    /// <paramref name="imageStream" /> as the portrait image bytes.
    /// </summary>
    public static IUserPortraitService CreateWithActivePortrait(
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

        return mock.Object;
    }
}
