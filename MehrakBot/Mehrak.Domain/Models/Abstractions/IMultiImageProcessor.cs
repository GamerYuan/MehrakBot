namespace Mehrak.Domain.Models.Abstractions;

public interface IMultiImageProcessor
{
    bool ShouldProcess { get; }
    Stream ProcessImage(IEnumerable<Stream> images);
}
