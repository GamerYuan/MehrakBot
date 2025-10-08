#region

using MehrakCore.Models;
using MehrakCore.Utility;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Bot.Executors.Hsr;

internal static class AvatarImageUtility
{
    private static readonly Font NormalFont;
    private static readonly Font SmallFont;

    private static readonly Color GoldBackgroundColor = Color.ParseHex("BC8F60");
    private static readonly Color PurpleBackgroundColor = Color.ParseHex("7651B3");
    private static readonly Color NormalConstColor = new Rgba32(69, 69, 69, 200);
    private static readonly Color GoldConstTextColor = Color.ParseHex("8A6500");

    static AvatarImageUtility()
    {
        var collection = new FontCollection();
        var fontFamily = collection.Add("Assets/Fonts/hsr.ttf");
        NormalFont = fontFamily.CreateFont(24, FontStyle.Bold);
        SmallFont = fontFamily.CreateFont(18, FontStyle.Regular);
    }

    public static Image<Rgba32> GetStyledAvatarImage(this HsrAvatar avatar, string text = "")
    {
        return GetStyledAvatarImageHelper(avatar.Rarity, avatar.Level, avatar.AvatarImage, avatar.Rank,
            text);
    }

    private static Image<Rgba32> GetStyledAvatarImageHelper(int rarity, int level, Image portrait, int rank,
        string text)
    {
        var avatarImage = new Image<Rgba32>(150, 180);
        var rectangle = new RectangleF(0, 146, 150, 30);

        avatarImage.Mutate(ctx =>
        {
            ctx.Fill(rarity == 4 ? PurpleBackgroundColor : GoldBackgroundColor);
            ctx.DrawImage(portrait, new Point(0, 0), 1f);
            ctx.Fill(Color.Black, rectangle);
            ctx.DrawText(new RichTextOptions(NormalFont)
            {
                Origin = new PointF(75, 174),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom
            }, string.IsNullOrEmpty(text) ? $"Lv. {level}" : text, Color.White);
            var constIcon = ImageUtility.CreateRoundedRectanglePath(30, 30, 5).Translate(115, 110);
            if (rank == 6)
            {
                ctx.Fill(Color.Gold, constIcon);
                ctx.DrawText(new RichTextOptions(NormalFont)
                {
                    Origin = new PointF(130, 126),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }, "6", GoldConstTextColor);
            }
            else if (rank > 0)
            {
                ctx.Fill(NormalConstColor, constIcon);
                ctx.DrawText(new RichTextOptions(NormalFont)
                    {
                        Origin = new PointF(130, 126),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }, $"{rank}", Color.White);
            }

            ctx.ApplyRoundedCorners(15);
        });

        return avatarImage;
    }
}
