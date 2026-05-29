#region

using Mehrak.Domain.Models.Abstractions;

#endregion

namespace Mehrak.Domain.Services.Abstractions;

public interface IImageUpdaterService
{
    Task<bool> UpdateImageAsync(IImageData data, IImageProcessor processor, CancellationToken cancellationToken = default);

    Task<bool> UpdateMultiImageAsync(IMultiImageData data, IMultiImageProcessor processor, CancellationToken cancellationToken = default);
}