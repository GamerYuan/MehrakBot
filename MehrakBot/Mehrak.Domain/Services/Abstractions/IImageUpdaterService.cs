#region

using Mehrak.Domain.Models.Abstractions;

#endregion

namespace Mehrak.Domain.Services.Abstractions;

public interface IImageUpdaterService
{
    public Task<bool> UpdateImageAsync(IImageData data, IImageProcessor processor);

    public Task<bool> UpdateMultiImageAsync(IMultiImageData data, IMultiImageProcessor processor);
}