namespace Mehrak.Domain.Models.Abstractions;

public interface IImageProcessor
{
    bool ShouldProcess { get; }

    Stream ProcessImage(Stream imageStream);
}