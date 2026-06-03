#region

using Mehrak.Application.Shared.Renderers.Extensions;
using Mehrak.Application.Shared.Utility;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Genshin;

internal static class AvatarImageUtility
{
    private static readonly Font NormalFont;
    private static readonly Font SmallFont;

    private static readonly Color GoldBackgroundColor = Color.FromPixel(new Rgb24(183, 125, 76));
    private static readonly Color PurpleBackgroundColor = Color.FromPixel(new Rgb24(132, 104, 173));
    private static readonly Color NormalConstColor = Color.FromPixel(new Rgba32(69, 69, 69, 200));
    private static readonly Color GoldConstTextColor = Color.ParseHex("8A6500");

    private static readonly DrawingOptions ClipOptions = new()
    {
        ShapeOptions = new ShapeOptions() { BooleanOperation = BooleanOperation.Intersection }
    };

    static AvatarImageUtility()
    {
        var collection = new FontCollection();
        var fontFamily = collection.Add("Assets/Fonts/genshin.ttf");
        NormalFont = fontFamily.CreateFont(24, FontStyle.Bold);
        SmallFont = fontFamily.CreateFont(18, FontStyle.Regular);
    }

    public static void DrawStyledAvatarImage(this GenshinAvatar avatar, DrawingCanvas canvas, Point location, string text = "")
    {
        using var region = canvas.CreateRegion(new Rectangle(location, new Size(150, 180)));
        var clipPath = new RoundedRectanglePolygon(new RectangleF(Point.Empty, new Size(150, 180)), 15);
        _ = region.Save(ClipOptions, clipPath);
        region.Fill(Brushes.Solid(avatar.Rarity == 4 ? PurpleBackgroundColor : GoldBackgroundColor));

        region.DrawImage(avatar.AvatarImage, avatar.AvatarImage.Bounds,
            new RectangleF(0, 0, 150, 150), KnownResamplers.Bicubic);
        region.Fill(Brushes.Solid(Color.PeachPuff), new Rectangle(0, 150, 150, 30));

        switch (avatar.AvatarType)
        {
            case 2:
                region.DrawRoundedRectangleOverlay(80, 35, new PointF(90, -10),
                    new RoundedRectangleOverlayStyle(Color.FromPixel(new Rgb24(225, 118, 128)), CornerRadius: 15));
                region.Restore();
                region.DrawText(new RichTextOptions(SmallFont)
                {
                    Origin = new PointF(98, 3),
                    VerticalAlignment = VerticalAlignment.Top
                }, "Trial", Brushes.Solid(Color.White), null);
                break;

            case 3:
                region.DrawRoundedRectangleOverlay(130, 35, new PointF(50, -10),
                    new RoundedRectangleOverlayStyle(Color.FromPixel(new Rgb24(73, 128, 185)), CornerRadius: 15));
                region.Restore();
                region.DrawText(new RichTextOptions(SmallFont)
                {
                    Origin = new PointF(63, 3),
                    VerticalAlignment = VerticalAlignment.Top
                }, "Support", Brushes.Solid(Color.White), null);
                break;

            default:
                region.Restore();
                break;
        }

        region.DrawText(new RichTextOptions(NormalFont)
        {
            Origin = new PointF(75, 180),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom
        }, string.IsNullOrEmpty(text) ? $"Lv. {avatar.Level}" : text, Brushes.Solid(Color.Black), null);

        if (avatar.Constellation > 0)
        {
            region.DrawRoundedRectangleOverlay(30, 30, new PointF(115, 115),
                new RoundedRectangleOverlayStyle(
                    avatar.Constellation == 6 ? Color.Gold : NormalConstColor,
                    CornerRadius: 5));
            region.DrawText(new RichTextOptions(NormalFont)
            {
                Origin = new PointF(130, 130),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
                avatar.Constellation.ToString(),
                Brushes.Solid(avatar.Constellation == 6 ? GoldConstTextColor : Color.White), null);
        }

    }
}
