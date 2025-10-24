using Mehrak.Domain.Models.Abstractions;

namespace Mehrak.Domain.Models;

public class ImageData : IImageData
{
    public string Name { get; }

    public string Url { get; }

    public ImageData(string name, string url)
    {
        Name = name;
        Url = url;
    }
}
