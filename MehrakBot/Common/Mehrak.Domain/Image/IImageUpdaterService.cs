#region

using Mehrak.Domain.Image.Abstractions;

#endregion

namespace Mehrak.Domain.Image;

public interface IImageUpdaterService
{
    Task<bool> UpdateImageAsync(IImageData data, IImageProcessor processor, CancellationToken cancellationToken = default);

    Task<bool> UpdateMultiImageAsync(IMultiImageData data, IMultiImageProcessor processor, CancellationToken cancellationToken = default);
}
