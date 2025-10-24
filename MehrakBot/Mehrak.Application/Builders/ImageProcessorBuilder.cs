using Mehrak.Application.Models;
using Mehrak.Domain.Models.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace Mehrak.Application.Builders;

public class ImageProcessorBuilder
{
    private readonly ImageProcessorBase m_ImageProcessor = new();

    public ImageProcessorBuilder Resize(int width, int height, IResampler? resampler = null)
    {
        m_ImageProcessor.AddOperation(ctx => ctx.Resize(width, height, resampler ?? KnownResamplers.Bicubic));
        return this;
    }

    public ImageProcessorBuilder Crop(int width, int height)
    {
        m_ImageProcessor.AddOperation(ctx => ctx.Crop(new Rectangle(0, 0, width, height)));
        return this;
    }

    public ImageProcessorBuilder Rotate(float angle)
    {
        m_ImageProcessor.AddOperation(ctx => ctx.Rotate(angle));
        return this;
    }

    public ImageProcessorBuilder Grayscale()
    {
        m_ImageProcessor.AddOperation(ctx => ctx.Grayscale());
        return this;
    }

    public ImageProcessorBuilder AddOperation(Action<IImageProcessingContext> operation)
    {
        m_ImageProcessor.AddOperation(operation);
        return this;
    }

    public IImageProcessor Build() => m_ImageProcessor;
}
