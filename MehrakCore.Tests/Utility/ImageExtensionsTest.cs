#region

using MehrakCore.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

#endregion

namespace MehrakCore.Test.Utility;

public class ImageExtensionsTest
{
    [Test]
    public void StandardizeImageSize_WithLargeSquareImage_ReturnsResizedImage()
    {
        Image<Rgba32> inputImage = new Image<Rgba32>(2000, 2000);
        inputImage.StandardizeImageSize(1000);
        Assert.That(inputImage.Size, Is.EqualTo(new Size(1000, 1000)));
    }

    [Test]
    public void StandardizeImageSize_WithSmallSquareImage_ReturnsResizedImage()
    {
        Image<Rgba32> inputImage = new Image<Rgba32>(500, 500);
        inputImage.StandardizeImageSize(1000);
        Assert.That(inputImage.Size, Is.EqualTo(new Size(1000, 1000)));
    }

    [Test]
    public void StandardizeImageSize_WithLargeRectangularImage_ReturnsResizedImage()
    {
        Image<Rgba32> inputImage = new Image<Rgba32>(2000, 1000);
        inputImage.StandardizeImageSize(1000);
        Assert.That(inputImage.Size, Is.EqualTo(new Size(1000, 1000)));
    }

    [Test]
    public void StandardizeImageSize_WithSmallRectangularImage_ReturnsResizedImage()
    {
        Image<Rgba32> inputImage = new Image<Rgba32>(500, 1000);
        inputImage.StandardizeImageSize(1000);
        Assert.That(inputImage.Size, Is.EqualTo(new Size(1000, 1000)));
    }
}
