#region

using Mehrak.Application.Builders;
using Mehrak.Domain.Models.Abstractions;

#endregion

namespace Mehrak.Application.Utility;

public static class ImageProcessors
{
    public static readonly IImageProcessor AvatarProcessor = new ImageProcessorBuilder()
        .Resize(150, 0)
        .Build();
}