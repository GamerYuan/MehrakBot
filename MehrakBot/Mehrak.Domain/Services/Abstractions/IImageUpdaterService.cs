using Mehrak.Domain.Models.Abstractions;

namespace Mehrak.Domain.Services.Abstractions;

public interface IImageUpdaterService
{
    public Task<bool> UpdateImageAsync(IImageData data, IImageProcessor processor);

    public Task<bool> UpdateMultiImageAsync(IMultiImageData data, IMultiImageProcessor processor);
}
