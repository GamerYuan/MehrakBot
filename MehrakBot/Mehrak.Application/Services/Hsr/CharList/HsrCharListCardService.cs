using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Hsr.Types;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json;

namespace Mehrak.Application.Services.Hsr.CharList;

internal class HsrCharListCardService : ICardService<IEnumerable<HsrCharacterInformation>>
{
    private readonly IImageRepository m_ImageRepository;
    private readonly ILogger<HsrCharListCardService> m_Logger;

    private static readonly JpegEncoder JpegEncoder = new()
    {
        Interleaved = false,
        Quality = 90,
        ColorType = JpegEncodingColor.Rgb
    };

    private readonly Font m_TitleFont;
    private readonly Font m_NormalFont;
    private readonly Font m_SmallFont;

    private static readonly Color GoldBackgroundColor = Color.ParseHex("BC8F60");
    private static readonly Color PurpleBackgroundColor = Color.ParseHex("7651B3");
    private static readonly Color BlueBackgroundColor = Color.FromRgb(90, 131, 187);
    private static readonly Color WhiteBackgroundColor = Color.FromRgb(128, 128, 130);

    private static readonly Color[] RarityColors =
    [
        WhiteBackgroundColor,
        BlueBackgroundColor,
        PurpleBackgroundColor,
        GoldBackgroundColor
    ];

    private static readonly Color PurpleForegroundColor = Color.FromRgb(204, 173, 255);

    private static readonly Color NormalConstColor = Color.FromRgba(69, 69, 69, 200);
    private static readonly Color GoldConstTextColor = Color.ParseHex("8A6500");

    private static readonly Color OverlayColor = Color.FromRgba(0, 0, 0, 128);
    private static readonly Color DarkOverlayColor = Color.FromRgba(0, 0, 0, 200);

    private static readonly string[] Elements = ["physical", "fire", "ice", "lightning", "wind", "quantum", "imaginary"];

    private static readonly Dictionary<string, Color> ElementForeground = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Fire", Color.FromRgb(253, 133, 101) },
        { "Ice", Color.FromRgb(176, 236, 249) },
        { "Lightning", Color.FromRgb(228, 134, 252) },
        { "Wind", Color.FromRgb(183, 234, 187) },
        { "Quantum", Color.FromRgb(196, 195, 248) },
        { "Imaginary", Color.FromRgb(255, 242, 156) },
        { "Physical", Color.FromRgb(236, 236, 236) }
    };

    private static readonly Dictionary<string, Color> ElementBackground = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Fire", Color.FromRgba(230, 41, 41, 128) },
        { "Ice", Color.FromRgba(38, 146, 211, 128) },
        { "Lightning", Color.FromRgba(184, 77, 211, 128) },
        { "Wind", Color.FromRgba(62, 177, 119, 128) },
        { "Quantum", Color.FromRgba(77, 69, 188, 128) },
        { "Imaginary", Color.FromRgba(245, 222, 53, 128) },
        { "Physical", Color.FromRgba(191, 195, 190, 128) }
    };

    public HsrCharListCardService(IImageRepository imageRepository, ILogger<HsrCharListCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_Logger = logger;

        FontCollection collection = new();
        FontFamily fontFamily = collection.Add("Assets/Fonts/hsr.ttf");

        m_TitleFont = fontFamily.CreateFont(40, FontStyle.Bold);
        m_NormalFont = fontFamily.CreateFont(28, FontStyle.Regular);
        m_SmallFont = fontFamily.CreateFont(20, FontStyle.Regular);
    }

    public async Task<Stream> GetCardAsync(ICardGenerationContext<IEnumerable<HsrCharacterInformation>> context)
    {
        m_Logger.LogInformation(LogMessage.CardGenStartInfo, "CharList", context.UserId);
        Stopwatch stopwatch = Stopwatch.StartNew();

        var charData = context.Data.ToList();
        List<IDisposable> disposables = [];
        try
        {
            m_Logger.LogInformation("Generating character list card for user {UserId} with {CharCount} characters",
                context.GameProfile.GameUid, charData.Count);
            Dictionary<int, Image> weaponImages = await charData.Where(x => x.Equip is not null).Select(x => x.Equip)
                .DistinctBy(x => x!.Id)
                .ToAsyncEnumerable()
                .SelectAwait(async x =>
                {
                    Image image = await Image.LoadAsync(
                        await m_ImageRepository.DownloadFileToStreamAsync(x!.ToImageName()));
                    return (x!.Id, Image: image);
                }).ToDictionaryAsync(x => x.Id, x => x.Image);
            disposables.AddRange(weaponImages.Values);

            ValueTask<List<Image<Rgba32>>> avatarImageTasks = charData.OrderByDescending(x => x.Level)
                .ThenByDescending(x => x.Rarity)
                .ThenBy(x => x.Name)
                .ToAsyncEnumerable()
                .SelectAwait(async x =>
                {
                    using Image avatarImage = await Image.LoadAsync(
                        await m_ImageRepository.DownloadFileToStreamAsync(x.ToImageName()));
                    return GetStyledCharacterImage(x, avatarImage, x.Equip is null ? null : weaponImages[x.Equip.Id]);
                })
                .ToListAsync();

            var charCountByElem = charData.GroupBy(x => x.Element!)
                .OrderBy(x => Array.IndexOf(Elements, x.Key))
                .Select(x => new { Element = x.Key, Count = x.Count() }).ToList();
            var charCountByRarity = charData.GroupBy(x => x.Rarity!.Value)
                .OrderBy(x => x.Key)
                .Select(x => new { Rarity = x.Key, Count = x.Count() }).ToList();

            List<Image<Rgba32>> avatarImages = await avatarImageTasks;

            disposables.AddRange(avatarImages);

            ImageUtility.GridLayout layout = ImageUtility.CalculateGridLayout(avatarImages.Count, 300, 180, [120, 50, 50, 50]);

            using Image<Rgba32> background = new(layout.OutputWidth, layout.OutputHeight + 50);

            background.Mutate(ctx =>
            {
                ctx.Clear(Color.FromRgb(69, 69, 69));
                ctx.DrawText(new RichTextOptions(m_TitleFont)
                {
                    Origin = new Vector2(50, 80),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, $"{context.GameProfile.Nickname} · AR {context.GameProfile.Level}", Color.White);

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(50, 110),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, context.GameProfile.GameUid!, Color.White);

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
                    ctx.Fill(RarityColors[entry.Rarity - 2].WithAlpha(128), overlay);
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
                context.GameProfile.GameUid, charData.Count);
            MemoryStream stream = new();
            await background.SaveAsJpegAsync(stream, JpegEncoder);
            stream.Position = 0;

            m_Logger.LogInformation(LogMessage.CardGenSuccess, "CharList", context.UserId,
                stopwatch.ElapsedMilliseconds);
            return stream;
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessage.CardGenError, "CharList", context.UserId, JsonSerializer.Serialize(context.Data));
            throw new CommandException("Failed to generate CharList card", e);
        }
        finally
        {
            disposables.ForEach(x => x.Dispose());
        }
    }

    private Image<Rgba32> GetStyledCharacterImage(HsrCharacterInformation charData, Image avatarImage,
        Image? weaponImage = null)
    {
        Image<Rgba32> background = new(300, 180);
        background.Mutate(ctx =>
        {
            ctx.Fill(RarityColors[charData.Rarity!.Value - 2], new RectangleF(0, 0, 150, 180));
            ctx.Fill(RarityColors[(charData.Equip?.Rarity - 2) ?? 0], new RectangleF(150, 0, 150, 180));

            ctx.DrawImage(avatarImage, new Point(0, 0), 1f);
            if (weaponImage is not null)
                ctx.DrawImage(weaponImage, new Point(150, 0), 1f);

            FontRectangle charLevelRect = TextMeasurer.MeasureSize($"Lv. {charData.Level}", new TextOptions(m_SmallFont));
            IPath charLevel =
                ImageUtility.CreateRoundedRectanglePath((int)charLevelRect.Width + 40, (int)charLevelRect.Height + 20,
                    10);
            ctx.Fill(DarkOverlayColor, charLevel.Translate(-25, 105));
            ctx.DrawText(new RichTextOptions(m_SmallFont)
            {
                Origin = new Vector2(5, 115 + (charLevelRect.Height / 2)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            }, $"Lv. {charData.Level}", Color.White);

            IPath constIcon = ImageUtility.CreateRoundedRectanglePath(30, 30, 5).Translate(115, 110);
            switch (charData.Rank)
            {
                case 6:
                    ctx.Fill(Color.Gold, constIcon);
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(130, 125),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }, "6", GoldConstTextColor);
                    break;

                case > 0:
                    ctx.Fill(NormalConstColor, constIcon);
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(130, 125),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }, $"{charData.Rank}", Color.White);
                    break;
            }

            if (charData.Equip is not null)
            {
                FontRectangle weapLevelRect = TextMeasurer.MeasureSize($"Lv. {charData.Equip.Level}", new TextOptions(m_SmallFont));
                IPath weapLevel =
                    ImageUtility.CreateRoundedRectanglePath((int)weapLevelRect.Width + 40, (int)weapLevelRect.Height + 20,
                        10);
                ctx.Fill(DarkOverlayColor, weapLevel.Translate(285 - weapLevelRect.Width, 105));
                ctx.DrawText(new RichTextOptions(m_SmallFont)
                {
                    Origin = new PointF(295 - (weapLevelRect.Width / 2), 115 + (weapLevelRect.Height / 2)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }, $"Lv. {charData.Equip.Level}", Color.White);

                IPath refineIcon = ImageUtility.CreateRoundedRectanglePath(30, 30, 5).Translate(155, 110);
                switch (charData.Equip.Rank)
                {
                    case 5:
                        ctx.Fill(Color.Gold, refineIcon);
                        ctx.DrawText(new RichTextOptions(m_NormalFont)
                        {
                            Origin = new Vector2(170, 125),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }, "5", GoldConstTextColor);
                        break;

                    case > 0:
                        ctx.Fill(NormalConstColor, refineIcon);
                        ctx.DrawText(new RichTextOptions(m_NormalFont)
                        {
                            Origin = new Vector2(170, 125),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }, $"{charData.Equip.Rank}", Color.White);
                        break;
                }
            }

            ctx.DrawLine(OverlayColor, 2f, new PointF(150, -5), new PointF(150, 185));
            ctx.BoxBlur(2, new Rectangle(147, 0, 5, 180));

            ctx.Fill(Color.Black, new RectangleF(0, 146, 300, 30));
            ctx.DrawText(new RichTextOptions(m_NormalFont)
            {
                Origin = new Vector2(150, 161),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }, $"{charData.Name}", Color.White);

            ctx.ApplyRoundedCorners(15);
        });

        return background;
    }
}
