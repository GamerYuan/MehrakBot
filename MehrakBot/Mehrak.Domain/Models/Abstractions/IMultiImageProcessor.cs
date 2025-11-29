namespace Mehrak.Domain.Models.Abstractions;

public interface IMultiImageProcessor : IImageProcessor
{
    Stream ProcessImage(IEnumerable<Stream> images);
}