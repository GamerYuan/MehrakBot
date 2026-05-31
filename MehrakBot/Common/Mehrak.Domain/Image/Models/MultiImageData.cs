#region

using Mehrak.Domain.Image.Abstractions;


#endregion

namespace Mehrak.Domain.Image.Models;

public class MultiImageData : IMultiImageData
{
    public string Name { get; }
    public string Url => string.Empty;
    public IEnumerable<string> AdditionalUrls { get; }

    public MultiImageData(string name, IEnumerable<string> urls)
    {
        Name = name;
        AdditionalUrls = urls;
    }
}
