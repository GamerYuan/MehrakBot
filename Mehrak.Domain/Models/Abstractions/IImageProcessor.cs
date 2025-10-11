namespace Mehrak.Domain.Models.Abstractions;

public interface IImageProcessor
{
    public bool ShouldProcess();

    public Stream ProcessImage(Stream imageStream);
}
