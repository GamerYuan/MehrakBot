#region

using Mehrak.Application.Shared.Renderers;
using Mehrak.Application.Shared.Renderers.Extensions;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Zzz;

internal static class AvatarImageUtility
{
    private static readonly AvatarImageStyle Style;

    private static readonly DrawingOptions ClipOptions = new()
    {
        ShapeOptions = new ShapeOptions() { BooleanOperation = BooleanOperation.Intersection }
    };

    static AvatarImageUtility()
    {
        var collection = new FontCollection();
        var fontFamily = collection.Add("Assets/Fonts/zzz.ttf");

        Style = new AvatarImageStyle(
            NormalFont: fontFamily.CreateFont(24, FontStyle.Bold),
            GoldBackgroundColor: Color.FromPixel(new Rgb24(188, 143, 96)),
            PurpleBackgroundColor: Color.FromPixel(new Rgb24(118, 81, 179)),
            NormalConstColor: Color.FromPixel(new Rgba32(69, 69, 69, 200)),
            GoldConstTextColor: Color.ParseHex("8A6500"),
            OverlayHeight: 35,
            DrawBorder: true,
            BorderWidth: 3f,
            BadgeOffsetY: 70,
            BadgeTextOffsetY: 55);
    }

    public static void DrawStyledAvatarImage(this ZzzAvatar avatar, DrawingCanvas canvas, Point location, string text = "")
    {
        AvatarImageRenderer.DrawStyledAvatar(canvas, avatar.AvatarImage, location,
            MapRarity(avatar.Rarity), avatar.Level, Style, avatar.Rank, text);
    }

    public static void DrawStyledBuddyImage(DrawingCanvas canvas, Point position, Image buddyImage)
    {
        var clipPath = new RoundedRectanglePolygon(new RectangleF(Point.Empty, new Size(150, 180)), 15);
        using var region = canvas.CreateRegion(new Rectangle(position, new Size(150, 180)));
        _ = region.Save(ClipOptions, clipPath);
        region.Fill(Brushes.Solid(Color.FromPixel(new Rgb24(24, 24, 24))));
        region.DrawImage(buddyImage, buddyImage.Bounds,
            new RectangleF(0, 0, buddyImage.Width, buddyImage.Height),
            KnownResamplers.Bicubic);
        region.Restore();
        region.Draw(Pens.Solid(Color.Black, 3f), clipPath);
    }

    private static int MapRarity(char rarity) => rarity switch
    {
        'S' => 5,
        'A' => 4,
        'B' => 3,
        _ => 1
    };
}
