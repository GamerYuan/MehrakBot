namespace Mehrak.Domain.Models.Abstractions;

public interface IMultiImageData : IImageData
{
    public IEnumerable<string> AdditionalUrls { get; }
}
