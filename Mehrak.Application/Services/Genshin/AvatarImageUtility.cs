#region

using Mehrak.Application.Models;
using MehrakCore.Utility;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Services.Genshin;

internal static class AvatarImageUtility
{
    private static readonly Font NormalFont;
    private static readonly Font SmallFont;

    private static readonly Color GoldBackgroundColor = new Rgb24(183, 125, 76);
    private static readonly Color PurpleBackgroundColor = new Rgb24(132, 104, 173);
    private static readonly Color NormalConstColor = new Rgba32(69, 69, 69, 200);
    private static readonly Color GoldConstTextColor = Color.ParseHex("8A6500");

    static AvatarImageUtility()
    {
        var collection = new FontCollection();
        var fontFamily = collection.Add("Assets/Fonts/genshin.ttf");
        NormalFont = fontFamily.CreateFont(24, FontStyle.Bold);
        SmallFont = fontFamily.CreateFont(18, FontStyle.Regular);
    }

    public static Image<Rgba32> GetStyledAvatarImage(this GenshinAvatar avatar, string text = "")
    {
        return GetStyledAvatarImageHelper(avatar.Rarity, avatar.Level, avatar.AvatarImage, avatar.Constellation,
            avatar.AvatarType, text);
    }

    private static Image<Rgba32> GetStyledAvatarImageHelper(int rarity, int level, Image portrait, int constellation,
        int avatarType, string text)
    {
        var avatarImage = new Image<Rgba32>(150, 180);
        var rectangle = new RectangleF(0, 150, 150, 30);

        avatarImage.Mutate(ctx =>
        {
            ctx.Fill(rarity == 4 ? PurpleBackgroundColor : GoldBackgroundColor);
            ctx.DrawImage(portrait, new Point(0, 0), 1f);
            ctx.Fill(Color.PeachPuff, rectangle);
            ctx.DrawText(new RichTextOptions(NormalFont)
            {
                Origin = new PointF(75, 180),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom
            }, string.IsNullOrEmpty(text) ? $"Lv. {level}" : text, Color.Black);
            var constIcon = ImageUtility.CreateRoundedRectanglePath(30, 30, 5).Translate(115, 115);
            if (constellation == 6)
            {
                ctx.Fill(Color.Gold, constIcon);
                ctx.DrawText(new RichTextOptions(NormalFont)
                {
                    Origin = new PointF(130, 130),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }, "6", GoldConstTextColor);
            }
            else if (constellation > 0)
            {
                ctx.Fill(NormalConstColor, constIcon);
                ctx.DrawText(new RichTextOptions(NormalFont)
                    {
                        Origin = new PointF(130, 130),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }, $"{constellation}", Color.White);
            }

            switch (avatarType)
            {
                case 2:
                    var trialOverlay = ImageUtility.CreateRoundedRectanglePath(80, 35, 15);
                    ctx.Fill(Color.FromRgb(225, 118, 128), trialOverlay.Translate(90, -10));
                    ctx.DrawText(new RichTextOptions(SmallFont)
                    {
                        Origin = new PointF(98, 3),
                        VerticalAlignment = VerticalAlignment.Top
                    }, "Trial", Color.White);
                    break;
                case 3:
                    var supportOverlay = ImageUtility.CreateRoundedRectanglePath(130, 35, 15);
                    ctx.Fill(Color.FromRgb(73, 128, 185), supportOverlay.Translate(50, -10));
                    ctx.DrawText(new RichTextOptions(SmallFont)
                    {
                        Origin = new PointF(63, 3),
                        VerticalAlignment = VerticalAlignment.Top
                    }, "Support", Color.White);
                    break;
            }

            ctx.ApplyRoundedCorners(15);
        });

        return avatarImage;
    }
}
