using MehrakCore.Models;
using MehrakCore.Utility;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MehrakCore.Services.Commands.Zzz;

internal static class AvatarImageUtility
{
    private static readonly Font NormalFont = new FontCollection().Add("Assets/Fonts/zzz.ttf").CreateFont(24, FontStyle.Bold);

    private static readonly Color GoldBackgroundColor = Color.ParseHex("BC8F60");
    private static readonly Color PurpleBackgroundColor = Color.ParseHex("7651B3");
    private static readonly Color NormalConstColor = new Rgba32(69, 69, 69, 200);
    private static readonly Color GoldConstTextColor = Color.ParseHex("8A6500");

    public static Image<Rgba32> GetStyledAvatarImage(this ZzzAvatar avatar, string text = "")
    {
        Image<Rgba32> avatarImage = new(150, 180);
        RectangleF rectangle = new(0, 145, 150, 35);

        avatarImage.Mutate(ctx =>
        {
            ctx.Fill(avatar.Rarity == 'A' ? PurpleBackgroundColor : GoldBackgroundColor);
            ctx.DrawImage(avatar.AvatarImage, new Point(0, 0), 1f);
            ctx.Fill(Color.Black, rectangle);
            ctx.DrawText(new RichTextOptions(NormalFont)
            {
                Origin = new PointF(75, 170),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }, string.IsNullOrEmpty(text) ? $"Lv. {avatar.Level}" : text, Color.White);
            IPath constIcon = ImageUtility.CreateRoundedRectanglePath(30, 30, 5).Translate(115, 110);
            if (avatar.Rank == 6)
            {
                ctx.Fill(Color.Gold, constIcon);
                ctx.DrawText(new RichTextOptions(NormalFont)
                {
                    Origin = new PointF(130, 130),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }, "6", GoldConstTextColor);
            }
            else if (avatar.Rank > 0)
            {
                ctx.Fill(NormalConstColor, constIcon);
                ctx.DrawText(new RichTextOptions(NormalFont)
                {
                    Origin = new PointF(130, 130),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }, $"{avatar.Rank}", Color.White);
            }

            IPath border = ImageUtility.CreateRoundedRectanglePath(150, 180, 15);
            ctx.Draw(Color.Black, 4, border);

            ctx.ApplyRoundedCorners(15);
        });

        return avatarImage;
    }
}
