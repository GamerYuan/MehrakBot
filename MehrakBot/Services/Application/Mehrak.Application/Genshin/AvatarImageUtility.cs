#region

using Mehrak.Application.Shared.Renderers;
using Mehrak.Application.Shared.Renderers.Extensions;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

#endregion

namespace Mehrak.Application.Genshin;

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
        var fontFamily = collection.Add(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets/Fonts/genshin.ttf"));

        Style = new AvatarImageStyle(
            NormalFont: fontFamily.CreateFont(24, FontStyle.Bold),
            SmallFont: fontFamily.CreateFont(18, FontStyle.Regular),
            GoldBackgroundColor: Color.FromPixel(new Rgb24(183, 125, 76)),
            PurpleBackgroundColor: Color.FromPixel(new Rgb24(132, 104, 173)),
            NormalConstColor: Color.FromPixel(new Rgba32(69, 69, 69, 200)),
            GoldConstTextColor: Color.ParseHex("8A6500"),
            OverlayColor: Color.PeachPuff,
            LevelTextColor: Color.Black,
            BadgeOffsetY: 65,
            BadgeTextOffsetY: 50,
            PostImageDrawer: DrawAvatarTypeBadge);
    }

    public static void DrawStyledAvatarImage(this GenshinAvatar avatar, DrawingCanvas canvas, Point location, string text = "")
    {
        AvatarImageRenderer.DrawStyledAvatar(canvas, avatar.AvatarImage, location,
            avatar.Rarity, avatar.Level, Style, avatar.Constellation, text,
            avatar.AvatarType);
    }

    private static void DrawAvatarTypeBadge(DrawingCanvas region, Point _, Size size, IPath clipPath, int avatarType)
    {
        if (avatarType is not (2 or 3))
            return;

        var (badgeWidth, badgeColor, badgeText, textX) = avatarType switch
        {
            2 => (80, Color.FromPixel(new Rgb24(225, 118, 128)), "Trial", 98f),
            3 => (130, Color.FromPixel(new Rgb24(73, 128, 185)), "Support", 63f),
            _ => default // unreachable
        };

        region.DrawRoundedRectangleOverlay(badgeWidth, 35, new PointF(size.Width - badgeWidth + 30, -10),
            new RoundedRectangleOverlayStyle(badgeColor, CornerRadius: 15));

        // Restore clip to draw text above the avatar bounds, then re-save
        region.Restore();
        region.DrawText(new RichTextOptions(Style.SmallFont!)
        {
            Origin = new PointF(textX, 3),
            VerticalAlignment = VerticalAlignment.Top
        }, badgeText, Brushes.Solid(Color.White), null);
        region.Save(ClipOptions, clipPath);
    }
}
