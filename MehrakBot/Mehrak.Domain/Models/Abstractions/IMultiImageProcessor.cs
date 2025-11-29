namespace Mehrak.Domain.Models.Abstractions;

public interface IMultiImageProcessor : IImageProcessor
{
    public Stream ProcessImage(IEnumerable<Stream> images);
}