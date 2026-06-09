namespace Mehrak.Domain.Image.Abstractions;

public interface IMultiImageProcessor
{
    bool ShouldProcess { get; }
    Stream ProcessImage(IEnumerable<Stream> images);
}
