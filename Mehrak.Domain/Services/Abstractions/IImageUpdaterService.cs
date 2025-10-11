using Mehrak.Domain.Models.Abstractions;

namespace Mehrak.Domain.Services.Abstractions;

public interface IImageUpdaterService
{
    public Task UpdateImageAsync(IImageData data, IImageProcessor processor);
}
