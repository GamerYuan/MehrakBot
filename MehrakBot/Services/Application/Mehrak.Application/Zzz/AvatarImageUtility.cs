#region

using Mehrak.Application.Shared.Renderers.Extensions;
using Mehrak.Application.Shared.Utility;
using Mehrak.GameApi.Zzz.Types;
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
    private static readonly Font NormalFont =
        new FontCollection().Add("Assets/Fonts/zzz.ttf").CreateFont(24, FontStyle.Bold);

    private static readonly Color GoldBackgroundColor = Color.ParseHex("BC8F60");
    private static readonly Color PurpleBackgroundColor = Color.ParseHex("7651B3");
    private static readonly Color NormalConstColor = Color.FromPixel(new Rgba32(69, 69, 69, 200));
    private static readonly Color GoldConstTextColor = Color.ParseHex("8A6500");

    public static Image<Rgba32> GetStyledAvatarImage(this ZzzAvatar avatar, string text = "")
    {
        Image<Rgba32> avatarImage = new(150, 180);
        RectangleF rectangle = new(0, 145, 150, 35);

        avatarImage.Mutate(ctx =>
        {
            ctx.Paint(canvas =>
            {
                canvas.Fill(Brushes.Solid(avatar.Rarity == 'A' ? PurpleBackgroundColor : GoldBackgroundColor), new Rectangle(0, 0, avatarImage.Width, avatarImage.Height));
            });

            ctx.Paint(canvas =>
            {
                canvas.DrawImage(avatar.AvatarImage, avatar.AvatarImage.Bounds,
                    new RectangleF(0, 0, avatar.AvatarImage.Width, avatar.AvatarImage.Height), KnownResamplers.Bicubic);
                canvas.Fill(Brushes.Solid(Color.Black), new Rectangle((int)rectangle.X, (int)rectangle.Y, (int)rectangle.Width, (int)rectangle.Height));
                canvas.DrawText(new RichTextOptions(NormalFont)
                {
                    Origin = new PointF(75, 160),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }, string.IsNullOrEmpty(text) ? $"Lv. {avatar.Level}" : text, Brushes.Solid(Color.White), null);
                if (avatar.Rank > 0)
                {
                    canvas.DrawRoundedRectangleOverlay(30, 30, new PointF(115, 110),
                        new RoundedRectangleOverlayStyle(
                            avatar.Rank == 6 ? Color.Gold : NormalConstColor,
                            CornerRadius: 5));
                    canvas.Restore(); // Restore before drawing text due to bug in ImageSharp.Drawing 3.0.0
                    canvas.DrawText(new RichTextOptions(NormalFont)
                    {
                        Origin = new PointF(130, 125),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                        avatar.Rank.ToString(),
                        Brushes.Solid(avatar.Rank == 6 ? GoldConstTextColor : Color.White), null);
                }

                var border = ImageUtility.CreateRoundedRectanglePath(150, 180, 15);
                canvas.Draw(Pens.Solid(Color.Black, 4), border);
            });

            ctx.ApplyRoundedCorners(15);
        });

        return avatarImage;
    }

    public static Image<Rgba32> GetStyledBuddyImage(this ZzzBuddyData buddy, Image buddyImage, Image starImage)
    {
        Image<Rgba32> background = new(150, 180);
        RectangleF rectangle = new(0, 145, 150, 35);

        background.Mutate(ctx =>
        {
            ctx.Paint(canvas =>
            {
                canvas.Fill(Brushes.Solid(Color.FromPixel(new Rgb24(24, 24, 24))), new Rectangle(0, 0, background.Width, background.Height));
            });

            ctx.Paint(canvas =>
            {
                var border = ImageUtility.CreateRoundedRectanglePath(150, 180, 15);

                canvas.DrawImage(buddyImage, buddyImage.Bounds,
                    new RectangleF(-45, -20, buddyImage.Width, buddyImage.Height), KnownResamplers.Bicubic);

                canvas.Fill(Brushes.Solid(Color.Black), new Rectangle((int)rectangle.X, (int)rectangle.Y, (int)rectangle.Width, (int)rectangle.Height));
                canvas.DrawText(new RichTextOptions(NormalFont)
                {
                    Origin = new PointF(75, 160),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }, $"Lv. {buddy.Level}", Brushes.Solid(Color.White), null);

                canvas.Draw(Pens.Solid(Color.Black, 4), border);
                canvas.DrawImage(starImage, starImage.Bounds,
                    new RectangleF(65, 120, starImage.Width, starImage.Height), KnownResamplers.Bicubic);
            });

            ctx.ApplyRoundedCorners(15);
        });
        return background;
    }
}
