#region

using Mehrak.Application.Shared.Renderers.Extensions;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Shared.Renderers;

public record AvatarImageStyle(
    Font NormalFont,
    Color GoldBackgroundColor,
    Color PurpleBackgroundColor,
    Color NormalConstColor,
    Color GoldConstTextColor,
    Font? SmallFont = null,
    Color GoldConstColor = default,
    Color OverlayColor = default,
    Color LevelTextColor = default,
    Size AvatarSize = default,
    int CornerRadius = 15,
    int OverlayHeight = 30,
    int OverlayY = -1,
    bool DrawBorder = false,
    float BorderWidth = 3f,
    Color BorderColor = default,
    int BadgeOffsetX = 35,
    int BadgeOffsetY = 65,
    int BadgeTextOffsetY = 50,
    Action<DrawingCanvas, Point, Size, IPath, int>? PostImageDrawer = null)
{
    public Color GoldConstColor { get; } = GoldConstColor == default ? Color.Gold : GoldConstColor;
    public Color OverlayColor { get; } = OverlayColor == default ? Color.Black : OverlayColor;
    public Color LevelTextColor { get; } = LevelTextColor == default ? Color.White : LevelTextColor;
    public Size AvatarSize { get; } = AvatarSize == default ? new Size(150, 180) : AvatarSize;
    public Color BorderColor { get; } = BorderColor == default ? Color.Black : BorderColor;
}

public static class AvatarImageRenderer
{
    private static readonly DrawingOptions ClipOptions = new()
    {
        ShapeOptions = new ShapeOptions() { BooleanOperation = BooleanOperation.Intersection }
    };

    /// <summary>
    /// Draws a styled avatar image with rarity background, level overlay, and optional constellation/rank badge.
    /// </summary>
    /// <param name="canvas">The drawing canvas.</param>
    /// <param name="avatarImage">The avatar image to draw.</param>
    /// <param name="location">Top-left position on the canvas.</param>
    /// <param name="rarity">Rarity value for background color (4 = purple, else gold).</param>
    /// <param name="characterLevel">Character level displayed in the overlay.</param>
    /// <param name="style">Rendering style configuration.</param>
    /// <param name="constellation">Constellation/rank level. Badge is drawn only if > 0.</param>
    /// <param name="text">Override text for the level overlay. If empty, displays "Lv. {level}".</param>
    /// <param name="postImageContext">Context value passed through to PostImageDrawer (e.g. Genshin AvatarType).</param>
    public static void DrawStyledAvatar(
        DrawingCanvas canvas,
        Image avatarImage,
        Point location,
        int rarity,
        int characterLevel,
        AvatarImageStyle style,
        int constellation = 0,
        string text = "",
        int postImageContext = 0)
    {
        var size = style.AvatarSize;

        using var region = canvas.CreateRegion(new Rectangle(location, size));
        var clipPath = new RoundedRectanglePolygon(new RectangleF(Point.Empty, size), style.CornerRadius);
        _ = region.Save(ClipOptions, clipPath);

        // Rarity background
        region.Fill(Brushes.Solid(rarity == 4 ? style.PurpleBackgroundColor : style.GoldBackgroundColor));

        // Avatar image
        region.DrawImage(avatarImage, avatarImage.Bounds,
            new RectangleF(0, 0, avatarImage.Width, avatarImage.Height), KnownResamplers.Bicubic);

        // Bottom overlay strip
        var overlayY = style.OverlayY >= 0 ? style.OverlayY : size.Height - style.OverlayHeight;
        region.Fill(Brushes.Solid(style.OverlayColor),
            new Rectangle(0, overlayY, size.Width, style.OverlayHeight));

        // Game-specific post-image drawing (e.g. Genshin Trial/Support badges).
        // Receives the clipPath and context value so the drawer can manage clip state if needed.
        style.PostImageDrawer?.Invoke(region, Point.Empty, size, clipPath, postImageContext);

        region.Restore();

        // Optional border around the clipped region
        if (style.DrawBorder)
        {
            region.Draw(Pens.Solid(style.BorderColor, style.BorderWidth), clipPath);
        }

        // Level / override text — visually centered within the overlay strip
        var displayText = string.IsNullOrEmpty(text) ? $"Lv. {characterLevel}" : text;
        var overlayCenterY = overlayY + style.OverlayHeight / 2f;
        var textBounds = TextMeasurer.MeasureBounds(displayText, new RichTextOptions(style.NormalFont)
        {
            Origin = PointF.Empty,
        });
        var textY = overlayCenterY - textBounds.Height / 2f;

        region.DrawText(new RichTextOptions(style.NormalFont)
        {
            Origin = new PointF(size.Width / 2f, textY),
            HorizontalAlignment = HorizontalAlignment.Center
        }, displayText, Brushes.Solid(style.LevelTextColor), null);

        // Constellation / rank badge
        if (constellation > 0)
        {
            region.DrawRoundedRectangleOverlay(30, 30,
                new PointF(size.Width - style.BadgeOffsetX, size.Height - style.BadgeOffsetY),
                new RoundedRectangleOverlayStyle(
                    constellation == 6 ? style.GoldConstColor : style.NormalConstColor,
                    CornerRadius: 5));

            region.DrawText(new RichTextOptions(style.NormalFont)
            {
                Origin = new PointF(size.Width - style.BadgeOffsetX + 15,
                    size.Height - style.BadgeTextOffsetY),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
                constellation.ToString(),
                Brushes.Solid(constellation == 6 ? style.GoldConstTextColor : Color.White), null);
        }
    }
}
