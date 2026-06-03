#region

using Mehrak.Application.Shared.Renderers;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

#endregion

namespace Mehrak.Application.Hsr;

internal static class AvatarImageUtility
{
    private static readonly AvatarImageStyle Style;

    static AvatarImageUtility()
    {
        var collection = new FontCollection();
        var fontFamily = collection.Add("Assets/Fonts/hsr.ttf");

        Style = new AvatarImageStyle(
            NormalFont: fontFamily.CreateFont(24, FontStyle.Bold),
            GoldBackgroundColor: Color.FromPixel(new Rgb24(188, 143, 96)),
            PurpleBackgroundColor: Color.FromPixel(new Rgb24(118, 81, 179)),
            NormalConstColor: Color.FromPixel(new Rgba32(69, 69, 69, 200)),
            GoldConstTextColor: Color.ParseHex("8A6500"),
            OverlayY: 146,
            BadgeOffsetY: 70,
            BadgeTextOffsetY: 54);
    }

    public static void DrawStyledAvatarImage(this HsrAvatar avatar, DrawingCanvas canvas, Point location, string text = "")
    {
        AvatarImageRenderer.DrawStyledAvatar(canvas, avatar.AvatarImage, location,
            avatar.Rarity, avatar.Level, Style, avatar.Rank, text);
    }
}
