#region

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ImageExtensions = MehrakCore.Utility.ImageExtensions;

#endregion

namespace MehrakCore.Test.Utility;

public class ImageExtensionsTest
{
    [Test]
    public void StandardizeImageSize_WithLargeSquareImage_ReturnsResizedImage()
    {
        using Image<Rgba32> inputImage = new Image<Rgba32>(2000, 2000);
        var output = ImageExtensions.StandardizeImageSize(inputImage, 1000);
        Assert.That(output.Size, Is.EqualTo(new Size(1000, 1000)));
    }

    [Test]
    public void StandardizeImageSize_WithSmallSquareImage_ReturnsResizedImage()
    {
        using Image<Rgba32> inputImage = new Image<Rgba32>(500, 500);
        var output = ImageExtensions.StandardizeImageSize(inputImage, 1000);
        Assert.That(output.Size, Is.EqualTo(new Size(1000, 1000)));
    }

    [Test]
    public void StandardizeImageSize_WithLargeRectangularImage_ReturnsResizedImage()
    {
        using Image<Rgba32> inputImage = new Image<Rgba32>(2000, 1000);
        var output = ImageExtensions.StandardizeImageSize(inputImage, 1000);
        Assert.That(output.Size, Is.EqualTo(new Size(1000, 1000)));
    }

    [Test]
    public void StandardizeImageSize_WithSmallRectangularImage_ReturnsResizedImage()
    {
        using Image<Rgba32> inputImage = new Image<Rgba32>(500, 1000);
        var output = ImageExtensions.StandardizeImageSize(inputImage, 1000);
        Assert.That(output.Size, Is.EqualTo(new Size(1000, 1000)));
    }
}
