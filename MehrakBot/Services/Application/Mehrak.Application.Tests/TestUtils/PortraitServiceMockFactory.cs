#region

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

#endregion

namespace Mehrak.Application.Tests.TestUtils;

/// <summary>
/// Helpers for creating stand-in portrait images in card tests.
/// </summary>
public static class PortraitServiceMockFactory
{
    /// <summary>
    /// Creates a PNG stream of the given size filled with a solid RGB color, for use as a
    /// stand-in portrait image in tests.
    /// </summary>
    public static MemoryStream CreateSolidColorPngStream(int width, int height, (byte R, byte G, byte B) color)
    {
        using var image = new Image<Rgb24>(width, height, new Rgb24(color.R, color.G, color.B));
        var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        ms.Position = 0;
        return ms;
    }
}
