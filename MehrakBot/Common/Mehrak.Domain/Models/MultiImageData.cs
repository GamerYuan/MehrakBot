#region

using Mehrak.Domain.Models.Abstractions;

#endregion

namespace Mehrak.Domain.Models;

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