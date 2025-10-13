using Mehrak.Application.Builders;
using Mehrak.Domain.Models.Abstractions;

namespace Mehrak.Application.Utility;

public static class ImageProcessors
{
    public static readonly IImageProcessor AvatarProcessor = new ImageProcessorBuilder()
        .Resize(150, 0)
        .Build();
}
