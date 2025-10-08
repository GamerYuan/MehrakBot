#region

using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Constants;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Numerics;
using System.Text.Json;

#endregion

namespace MehrakCore.Services.Commands.Genshin.CharList;

public class GenshinCharListCardService : ICommandService<GenshinCharListCommandExecutor>
{
    private readonly ImageRepository m_ImageRepository;
    private readonly ILogger<GenshinCharListCardService> m_Logger;

    private static readonly JpegEncoder JpegEncoder = new()
    {
        Interleaved = false,
        Quality = 90,
        ColorType = JpegEncodingColor.Rgb
    };

    private readonly Font m_TitleFont;
    private readonly Font m_NormalFont;
    private readonly Font m_SmallFont;

    private static readonly Color GoldBackgroundColor = Color.FromRgb(183, 125, 76);
    private static readonly Color PurpleBackgroundColor = Color.FromRgb(132, 104, 173);
    private static readonly Color BlueBackgroundColor = Color.FromRgb(86, 130, 166);
    private static readonly Color GreenBackgroundColor = Color.FromRgb(79, 135, 111);
    private static readonly Color WhiteBackgroundColor = Color.FromRgb(128, 128, 130);

    private static readonly Color[] RarityColors =
    [
        WhiteBackgroundColor,
        GreenBackgroundColor,
        BlueBackgroundColor,
        PurpleBackgroundColor,
        GoldBackgroundColor
    ];

    private static readonly Color PurpleForegroundColor = Color.FromRgb(204, 173, 255);

    private static readonly Color NormalConstColor = Color.FromRgba(69, 69, 69, 200);
    private static readonly Color GoldConstTextColor = Color.FromRgb(138, 101, 0);

    private static readonly Color OverlayColor = Color.FromRgba(0, 0, 0, 128);
    private static readonly Color DarkOverlayColor = Color.FromRgba(0, 0, 0, 200);

    private static readonly string[] Elements =
    [
        "Pyro", "Hydro", "Cryo", "Electro", "Anemo", "Geo", "Dendro"
    ];

    private static readonly Dictionary<string, Color> ElementForeground = new()
    {
        { "Pyro", Color.FromRgb(244, 163, 111) },
        { "Hydro", Color.FromRgb(7, 229, 252) },
        { "Cryo", Color.FromRgb(203, 253, 253) },
        { "Electro", Color.FromRgb(222, 186, 255) },
        { "Anemo", Color.FromRgb(163, 238, 202) },
        { "Geo", Color.FromRgb(242, 213, 95) },
        { "Dendro", Color.FromRgb(172, 230, 40) }
    };

    private static readonly Dictionary<string, Color> ElementBackground = new()
    {
        { "Pyro", Color.FromRgba(198, 90, 21, 128) },
        { "Hydro", Color.FromRgba(25, 156, 198, 128) },
        { "Cryo", Color.FromRgba(108, 192, 192, 128) },
        { "Electro", Color.FromRgba(177, 117, 217, 128) },
        { "Anemo", Color.FromRgba(56, 185, 145, 128) },
        { "Geo", Color.FromRgba(179, 132, 36, 128) },
        { "Dendro", Color.FromRgba(128, 175, 18, 128) }
    };

    public GenshinCharListCardService(ImageRepository imageRepository, ILogger<GenshinCharListCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_Logger = logger;

        FontCollection collection = new();
        FontFamily fontFamily = collection.Add("Assets/Fonts/genshin.ttf");

        m_TitleFont = fontFamily.CreateFont(40, FontStyle.Bold);
        m_NormalFont = fontFamily.CreateFont(28, FontStyle.Regular);
        m_SmallFont = fontFamily.CreateFont(20, FontStyle.Regular);
    }

    public async ValueTask<Stream> GetCharListCardAsync(UserGameData gameData,
        List<GenshinBasicCharacterData> charData)
    {
        List<IDisposable> disposables = [];
        try
        {
            m_Logger.LogInformation("Generating character list card for user {UserId} with {CharCount} characters",
                gameData.GameUid, charData.Count);

            Dictionary<int, Image> weaponImages = await charData.Select(x => x.Weapon).DistinctBy(x => x.Id).ToAsyncEnumerable()
                .ToDictionaryAwaitAsync(async x => await Task.FromResult(x.Id!.Value),
                    async x =>
                    {
                        Image image = await Image.LoadAsync(
                            await m_ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.GenshinFileName, x.Id)));
                        image.Mutate(ctx => ctx.Resize(150, 0, KnownResamplers.Bicubic));
                        return image;
                    });
            disposables.AddRange(weaponImages.Values);

            ValueTask<List<Image<Rgba32>>> avatarImageTask = charData.OrderByDescending(x => x.Level)
                .ThenByDescending(x => x.Rarity)
                .ThenBy(x => x.Name)
                .ToAsyncEnumerable()
                .SelectAwait(async x =>
                {
                    using Image avatarImage = await Image.LoadAsync(
                        await m_ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.GenshinAvatarName, x.Id)));
                    return GetStyledCharacterImage(x, avatarImage, weaponImages[x.Weapon.Id!.Value]);
                })
                .ToListAsync();

            var charCountByElem = charData.GroupBy(x => x.Element!)
                .OrderBy(x => Array.IndexOf(Elements, x.Key))
                .Select(x => new { Element = x.Key, Count = x.Count() }).ToList();
            var charCountByRarity = charData.GroupBy(x => x.Rarity!.Value)
                .OrderBy(x => x.Key)
                .Select(x => new { Rarity = x.Key, Count = x.Count() }).ToList();

            List<Image<Rgba32>> avatarImages = await avatarImageTask;

            disposables.AddRange(avatarImages);

            ImageUtility.GridLayout layout = ImageUtility.CalculateGridLayout(avatarImages.Count, 300, 180, [120, 50, 50, 50]);

            Image<Rgba32> background = new(layout.OutputWidth, layout.OutputHeight + 50);

            background.Mutate(ctx =>
            {
                ctx.Clear(Color.FromRgb(69, 69, 69));
                ctx.DrawText(new RichTextOptions(m_TitleFont)
                {
                    Origin = new Vector2(50, 80),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, $"{gameData.Nickname}·AR {gameData.Level}", Color.White);

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(50, 110),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, gameData.GameUid!, Color.White);

                foreach (ImageUtility.ImagePosition position in layout.ImagePositions)
                {
                    Image<Rgba32> image = avatarImages[position.ImageIndex];
                    ctx.DrawImage(image, new Point(position.X, position.Y), 1f);
                }

                int yOffset = layout.OutputHeight - 30;
                int xOffset = 50;
                foreach (var entry in charCountByElem)
                {
                    FontRectangle countSize = TextMeasurer.MeasureSize(entry.Count.ToString(),
                        new TextOptions(m_NormalFont));
                    FontRectangle elemSize = TextMeasurer.MeasureSize(entry.Element, new TextOptions(m_NormalFont));
                    FontRectangle size = new(0, 0, countSize.Width + elemSize.Width + 20,
                        countSize.Height + elemSize.Height);
                    IPath overlay =
                        ImageUtility.CreateRoundedRectanglePath((int)size.Width + 50, 50, 10)
                            .Translate(xOffset, yOffset);
                    EllipsePolygon foreground = new(new PointF(xOffset + 20, yOffset + 25), 10);
                    ctx.Fill(ElementBackground[entry.Element], overlay);
                    ctx.Fill(ElementForeground[entry.Element], foreground);
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(xOffset + 40, yOffset + 26),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Center
                    }, entry.Element, Color.White);
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(xOffset + 35 + size.Width, yOffset + 26),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center
                    }, entry.Count.ToString(), Color.White);
                    xOffset += (int)size.Width + 70;
                }

                foreach (var entry in charCountByRarity)
                {
                    FontRectangle countSize = TextMeasurer.MeasureSize(entry.Count.ToString(),
                        new TextOptions(m_NormalFont));
                    FontRectangle elemSize = TextMeasurer.MeasureSize($"{entry.Rarity} Star", new TextOptions(m_NormalFont));
                    FontRectangle size = new(0, 0, countSize.Width + elemSize.Width + 20,
                        countSize.Height + elemSize.Height);
                    IPath overlay =
                        ImageUtility.CreateRoundedRectanglePath((int)size.Width + 50, 50, 10)
                            .Translate(xOffset, yOffset);
                    EllipsePolygon foreground = new(new PointF(xOffset + 20, yOffset + 25), 10);
                    ctx.Fill(RarityColors[entry.Rarity - 1].WithAlpha(128), overlay);
                    ctx.Fill(entry.Rarity == 5 ? Color.Gold : PurpleForegroundColor, foreground);
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(xOffset + 40, yOffset + 26),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Center
                    }, $"{entry.Rarity} Star", Color.White);
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(xOffset + 35 + size.Width, yOffset + 26),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center
                    }, entry.Count.ToString(), Color.White);
                    xOffset += (int)size.Width + 70;
                }
            });

            m_Logger.LogInformation("Completed character list card for user {UserId} with {CharCount} characters",
                gameData.GameUid, charData.Count);
            MemoryStream stream = new();
            await background.SaveAsJpegAsync(stream, JpegEncoder);
            stream.Position = 0;
            return stream;
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Failed to get character list card for uid {UserId}\n{CharData}", gameData.GameUid,
                JsonSerializer.Serialize(charData));
            throw;
        }
        finally
        {
            foreach (IDisposable disposable in disposables) disposable.Dispose();
        }
    }

    private Image<Rgba32> GetStyledCharacterImage(GenshinBasicCharacterData charData, Image avatarImage,
        Image weaponImage)
    {
        Image<Rgba32> background = new(300, 180);
        background.Mutate(ctx =>
        {
            ctx.Fill(RarityColors[charData.Rarity!.Value - 1], new RectangleF(0, 0, 150, 180));
            ctx.Fill(RarityColors[charData.Weapon.Rarity!.Value - 1], new RectangleF(150, 0, 150, 180));

            ctx.DrawImage(avatarImage, new Point(0, 0), 1f);
            ctx.DrawImage(weaponImage, new Point(150, 0), 1f);

            FontRectangle charLevelRect = TextMeasurer.MeasureSize($"Lv. {charData.Level}", new TextOptions(m_SmallFont));
            IPath charLevel =
                ImageUtility.CreateRoundedRectanglePath((int)charLevelRect.Width + 40, (int)charLevelRect.Height + 20,
                    10);
            ctx.Fill(DarkOverlayColor, charLevel.Translate(-25, 110));
            ctx.DrawText(new RichTextOptions(m_SmallFont)
            {
                Origin = new Vector2(5, 120 + charLevelRect.Height / 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            }, $"Lv. {charData.Level}", Color.White);

            IPath constIcon = ImageUtility.CreateRoundedRectanglePath(30, 30, 5).Translate(115, 115);
            switch (charData.ActivedConstellationNum)
            {
                case 6:
                    ctx.Fill(Color.Gold, constIcon);
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(130, 130),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }, "6", GoldConstTextColor);
                    break;

                case > 0:
                    ctx.Fill(NormalConstColor, constIcon);
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(130, 130),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }, $"{charData.ActivedConstellationNum}", Color.White);
                    break;
            }

            FontRectangle weapLevelRect = TextMeasurer.MeasureSize($"Lv. {charData.Weapon.Level}", new TextOptions(m_SmallFont));
            IPath weapLevel =
                ImageUtility.CreateRoundedRectanglePath((int)weapLevelRect.Width + 40, (int)weapLevelRect.Height + 20,
                    10);
            ctx.Fill(DarkOverlayColor, weapLevel.Translate(285 - weapLevelRect.Width, 110));
            ctx.DrawText(new RichTextOptions(m_SmallFont)
            {
                Origin = new PointF(295 - weapLevelRect.Width / 2, 120 + weapLevelRect.Height / 2),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }, $"Lv. {charData.Weapon.Level}", Color.White);

            IPath refineIcon = ImageUtility.CreateRoundedRectanglePath(30, 30, 5).Translate(155, 115);
            switch (charData.Weapon.AffixLevel)
            {
                case 5:
                    ctx.Fill(Color.Gold, refineIcon);
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(170, 130),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }, "5", GoldConstTextColor);
                    break;

                case > 0:
                    ctx.Fill(NormalConstColor, refineIcon);
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(170, 130),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }, $"{charData.Weapon.AffixLevel}", Color.White);
                    break;
            }

            ctx.DrawLine(OverlayColor, 2f, new PointF(150, -5), new PointF(150, 185));
            ctx.BoxBlur(2, new Rectangle(147, 0, 5, 180));

            ctx.Fill(Color.PeachPuff, new RectangleF(0, 150, 300, 30));
            ctx.DrawText(new RichTextOptions(m_NormalFont)
            {
                Origin = new Vector2(150, 165),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }, $"{charData.Name}", Color.Black);

            ctx.ApplyRoundedCorners(15);
        });

        return background;
    }
}
