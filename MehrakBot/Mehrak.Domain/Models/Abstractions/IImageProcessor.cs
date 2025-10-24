namespace Mehrak.Domain.Models.Abstractions;

public interface IImageProcessor
{
    public bool ShouldProcess { get; }

    public Stream ProcessImage(Stream imageStream);
}
