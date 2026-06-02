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

namespace Mehrak.Application.Hsr;

internal static class AvatarImageUtility
{
    private static readonly Font NormalFont;
    private static readonly Font SmallFont;

    private static readonly Color GoldBackgroundColor = Color.FromPixel(new Rgb24(188, 143, 96));
    private static readonly Color PurpleBackgroundColor = Color.FromPixel(new Rgb24(118, 81, 179));
    private static readonly Color NormalConstColor = Color.FromPixel(new Rgba32(69, 69, 69, 200));
    private static readonly Color GoldConstTextColor = Color.ParseHex("8A6500");

    private static readonly DrawingOptions ClipOptions = new()
    {
        ShapeOptions = new ShapeOptions() { BooleanOperation = BooleanOperation.Intersection }
    };

    static AvatarImageUtility()
    {
        var collection = new FontCollection();
        var fontFamily = collection.Add("Assets/Fonts/hsr.ttf");
        NormalFont = fontFamily.CreateFont(24, FontStyle.Bold);
        SmallFont = fontFamily.CreateFont(18, FontStyle.Regular);
    }

    public static void DrawStyledAvatarImage(this HsrAvatar avatar, DrawingCanvas canvas, Point location, string text = "")
    {
        using var region = canvas.CreateRegion(new Rectangle(location, new Size(150, 180)));
        var clipPath = new RoundedRectanglePolygon(new RectangleF(Point.Empty, new Size(150, 180)), 15);
        _ = region.Save(ClipOptions, clipPath);
        region.Fill(Brushes.Solid(avatar.Rarity == 4 ? PurpleBackgroundColor : GoldBackgroundColor));

        region.DrawImage(avatar.AvatarImage, avatar.AvatarImage.Bounds,
            new RectangleF(0, 0, avatar.AvatarImage.Width, avatar.AvatarImage.Height), KnownResamplers.Bicubic);
        region.Fill(Brushes.Solid(Color.Black), new Rectangle(0, 146, 150, 30));
        region.Restore();

        region.DrawText(new RichTextOptions(NormalFont)
        {
            Origin = new PointF(75, 174),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom
        }, string.IsNullOrEmpty(text) ? $"Lv. {avatar.Level}" : text, Brushes.Solid(Color.White), null);

        if (avatar.Rank > 0)
        {
            region.DrawRoundedRectangleOverlay(30, 30, new PointF(115, 110),
                new RoundedRectangleOverlayStyle(
                    avatar.Rank == 6 ? Color.Gold : NormalConstColor,
                    CornerRadius: 5));
            region.DrawText(new RichTextOptions(NormalFont)
            {
                Origin = new PointF(130, 126),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
                avatar.Rank.ToString(),
                Brushes.Solid(avatar.Rank == 6 ? GoldConstTextColor : Color.White), null);
        }
    }

    [Obsolete]
    public static Image<Rgba32> GetStyledAvatarImage(this HsrAvatar avatar, string text = "")
    {
        return GetStyledAvatarImageHelper(avatar.Rarity, avatar.Level, avatar.AvatarImage, avatar.Rank,
            text);
    }

    [Obsolete]
    private static Image<Rgba32> GetStyledAvatarImageHelper(int rarity, int level, Image portrait, int rank,
        string text)
    {
        var avatarImage = new Image<Rgba32>(150, 180);
        var rectangle = new Rectangle(0, 146, 150, 30);

        avatarImage.Mutate(ctx =>
        {
            ctx.Paint(canvas => canvas.Fill(Brushes.Solid(rarity == 4 ? PurpleBackgroundColor : GoldBackgroundColor), new Rectangle(0, 0, avatarImage.Width, avatarImage.Height)));

            ctx.Paint(canvas =>
            {
                canvas.DrawImage(portrait, portrait.Bounds,
                    new RectangleF(0, 0, portrait.Width, portrait.Height), KnownResamplers.Bicubic);
                canvas.Fill(Brushes.Solid(Color.Black), rectangle);
                canvas.DrawText(new RichTextOptions(NormalFont)
                {
                    Origin = new PointF(75, 174),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom
                }, string.IsNullOrEmpty(text) ? $"Lv. {level}" : text, Brushes.Solid(Color.White), null);
                if (rank > 0)
                {
                    canvas.DrawRoundedRectangleOverlay(30, 30, new PointF(115, 110),
                        new RoundedRectangleOverlayStyle(
                            rank == 6 ? Color.Gold : NormalConstColor,
                            CornerRadius: 5));
                    canvas.Restore(); // Restore before drawing text due to bug in ImageSharp.Drawing 3.0.0
                    canvas.DrawText(new RichTextOptions(NormalFont)
                    {
                        Origin = new PointF(130, 126),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                        rank.ToString(),
                        Brushes.Solid(rank == 6 ? GoldConstTextColor : Color.White), null);
                }
            });

            ctx.ApplyRoundedCorners(15);
        });

        return avatarImage;
    }
}
