namespace Mehrak.Domain.Models.Abstractions;

public interface IMultiImageData : IImageData
{
    IEnumerable<string> AdditionalUrls { get; }
}