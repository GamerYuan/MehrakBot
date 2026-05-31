namespace Mehrak.Domain.Image.Abstractions;

public interface IImageProcessor
{
    bool ShouldProcess { get; }

    Stream ProcessImage(Stream imageStream);
}
