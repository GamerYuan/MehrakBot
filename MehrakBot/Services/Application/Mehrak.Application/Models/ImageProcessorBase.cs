#region

using Mehrak.Domain.Models.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Models;

internal class ImageProcessorBase : IImageProcessor
{
    public bool ShouldProcess => m_Operations.Count > 0;

    private readonly List<Action<IImageProcessingContext>> m_Operations = [];

    public Stream ProcessImage(Stream imageStream)
    {
        using var image = Image.Load(imageStream);
        image.Mutate(ctx =>
        {
            foreach (var operation in m_Operations) operation(ctx);
        });

        var outputStream = new MemoryStream();
        image.SaveAsPng(outputStream);
        outputStream.Position = 0;
        return outputStream;
    }

    internal void AddOperation(Action<IImageProcessingContext> operation)
    {
        m_Operations.Add(operation);
    }
}