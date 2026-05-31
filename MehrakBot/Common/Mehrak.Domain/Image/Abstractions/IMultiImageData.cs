namespace Mehrak.Domain.Image.Abstractions;

public interface IMultiImageData : IImageData
{
    IEnumerable<string> AdditionalUrls { get; }
}
