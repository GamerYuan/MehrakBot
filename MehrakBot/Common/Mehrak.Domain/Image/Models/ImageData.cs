#region

using Mehrak.Domain.Image.Abstractions;


#endregion

namespace Mehrak.Domain.Image.Models;

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