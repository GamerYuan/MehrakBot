#region

using Mehrak.Domain.Models.Abstractions;
using SixLabors.ImageSharp;

#endregion

namespace Mehrak.Application.Models;

internal class MultiImageProcessorBase : IMultiImageProcessor
{
    public bool ShouldProcess => Operation != null;

    private Action<List<Image>>? Operation { get; set; }

    public Stream ProcessImage(IEnumerable<Stream> images)
    {
        var imageStreams = images.Select(x => Image.Load(x)).ToList();

        if (Operation != null) Operation(imageStreams);

        var ret = new MemoryStream();
        imageStreams[0].SaveAsPng(ret);
        imageStreams.ForEach(x => x.Dispose());

        ret.Position = 0;
        return ret;
    }

    public Stream ProcessImage(Stream imageStream)
    {
        throw new NotSupportedException();
    }

    public void SetOperation(Action<List<Image>> operation)
    {
        Operation = operation;
    }
}