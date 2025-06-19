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
    private static readonly Font NormalFont;

    private static readonly Color NormalConstColor;
    private static readonly Color GoldConstTextColor;

    static AvatarImageUtility()
    {
        var collection = new FontCollection();
        var fontFamily = collection.Add("Fonts/genshin.ttf");
        NormalFont = fontFamily.CreateFont(24, FontStyle.Bold);

        NormalConstColor = new Rgba32(69, 69, 69, 200);
        GoldConstTextColor = Color.ParseHex("8A6500");
    }

    public static Image<Rgba32> GetStyledAvatarImage(this Avatar avatar, Image portrait, int constellation = 0)
    {
        if (portrait == null) throw new ArgumentNullException(nameof(portrait), "Portrait image cannot be null");

        var avatarImage = new Image<Rgba32>(150, 180);
        var rectangle = new RectangleF(0, 150, 150, 30);

        avatarImage.Mutate(ctx =>
        {
            ctx.Fill(avatar.Rarity!.Value == 4 ? Color.Purple : Color.Gold);
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
}
