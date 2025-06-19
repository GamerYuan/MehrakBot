#region

using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Utility;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ImageExtensions = MehrakCore.Utility.ImageExtensions;

#endregion

namespace MehrakCore.Services.Commands.Genshin;

internal static class AvatarImageUtility
{
    private static readonly Image<Rgba32> FourStarAvatarBackground = new(150, 180);
    private static readonly Image<Rgba32> FiveStarAvatarBackground = new(150, 180);
    private static readonly Font NormalFont;

    private static readonly Color NormalConstColor;
    private static readonly Color GoldConstTextColor;

    static AvatarImageUtility()
    {
        FourStarAvatarBackground.Mutate(x => { x.Fill(Color.Purple); });
        FiveStarAvatarBackground.Mutate(x => { x.Fill(Color.Gold); });

        var collection = new FontCollection();
        var fontFamily = collection.Add("Fonts/genshin.ttf");
        NormalFont = fontFamily.CreateFont(24, FontStyle.Bold);

        NormalConstColor = Rgba32.ParseHex("454545BF");
        GoldConstTextColor = Color.ParseHex("AD7F00");
    }

    public static Image<Rgba32> GetStyledAvatarImage(this Avatar avatar, Image portrait, int constellation = 0)
    {
        if (portrait == null) throw new ArgumentNullException(nameof(portrait), "Portrait image cannot be null");

        var avatarImage = GetAvatarBackground(avatar.Rarity!.Value);
        var rectangle = new RectangleF(0, 150, 150, 30);

        avatarImage.Mutate(ctx =>
        {
            ctx.DrawImage(portrait, new Point(0, 0), 1f);
            ctx.Fill(Color.PeachPuff, rectangle);
            ctx.DrawText(new RichTextOptions(NormalFont)
                {
                    Origin = new PointF(75, 180),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom
                }, $"Lv. {avatar.Level}", Color.Black);
            var constIcon = ImageExtensions.CreateRoundedRectanglePath(30, 30, 5).Translate(115, 115);
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

            ctx.ApplyRoundedCorners(15);
        });

        return avatarImage;
    }

    private static Image<Rgba32> GetAvatarBackground(int rarity)
    {
        return rarity switch
        {
            4 => FourStarAvatarBackground.Clone(),
            5 => FiveStarAvatarBackground.Clone(),
            _ => throw new ArgumentOutOfRangeException(nameof(rarity), "Invalid rarity value")
        };
    }
}
