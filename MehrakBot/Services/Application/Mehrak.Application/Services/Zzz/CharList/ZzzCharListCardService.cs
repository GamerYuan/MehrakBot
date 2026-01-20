using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Mehrak.Application.Models;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Zzz.Types;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Mehrak.Application.Services.Zzz.CharList;

public class ZzzCharListCardService : ICardService<(IEnumerable<ZzzBasicAvatarData>, IEnumerable<ZzzBuddyData>)>, IAsyncInitializable
{
    private readonly IImageRepository m_ImageRepository;
    private readonly IApplicationMetrics m_Metrics;
    private readonly ILogger<ZzzCharListCardService> m_Logger;

    private static readonly JpegEncoder JpegEncoder = new()
    {
        Interleaved = false,
        Quality = 90,
        ColorType = JpegEncodingColor.Rgb
    };

    private readonly Font m_TitleFont;
    private readonly Font m_NormalFont;

    private Dictionary<int, Image> m_StarImages = [];

    private static readonly Dictionary<string, Color> ElementForeground = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Physical", Color.FromRgb(244, 163, 111) },
        { "Fire", Color.FromRgb(7, 229, 252) },
        { "Ice", Color.FromRgb(203, 253, 253) },
        { "Electric", Color.FromRgb(222, 186, 255) },
        { "Ether", Color.FromRgb(163, 238, 202) },
    };

    private static readonly Dictionary<string, Color> ElementBackground = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Physical", Color.FromRgba(198, 90, 21, 128) },
        { "Fire", Color.FromRgba(25, 156, 198, 128) },
        { "Ice", Color.FromRgba(108, 192, 192, 128) },
        { "Electric", Color.FromRgba(177, 117, 217, 128) },
        { "Ether", Color.FromRgba(56, 185, 145, 128) },
    };

    private static readonly char[] RarityOrder = ['S', 'A'];

    private static readonly Color GoldBackgroundColor = Color.FromRgb(183, 125, 76);
    private static readonly Color PurpleBackgroundColor = Color.FromRgb(132, 104, 173);

    private static readonly Color PurpleForegroundColor = Color.FromRgb(204, 173, 255);

    private static readonly TextInfo TextInfo = new CultureInfo("en-US", false).TextInfo;

    public ZzzCharListCardService(IImageRepository imageRepository, IApplicationMetrics metrics, ILogger<ZzzCharListCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_Metrics = metrics;
        m_Logger = logger;

        FontCollection collection = new();
        var fontFamily = collection.Add("Assets/Fonts/zzz.ttf");

        m_TitleFont = fontFamily.CreateFont(40, FontStyle.Bold);
        m_NormalFont = fontFamily.CreateFont(28, FontStyle.Regular);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        List<Task<(int x, Image)>> starTasks =
        [
            .. Enumerable.Range(1, 5)
                .Select(async i => (i, await Image.LoadAsync(
                    await m_ImageRepository.DownloadFileToStreamAsync($"zzz_weapon_star_{i}"))))
        ];

        var starImage = await Task.WhenAll(starTasks);
        foreach (var item in starImage)
        {
            item.Item2.Mutate(x => x.Resize(0, 30));
        }

        m_StarImages = starImage.ToDictionary();

        m_Logger.LogInformation(LogMessage.ServiceInitialized, nameof(ZzzCharListCardService));
    }

    public async Task<Stream> GetCardAsync(ICardGenerationContext<(IEnumerable<ZzzBasicAvatarData>, IEnumerable<ZzzBuddyData>)> context)
    {
        using var cardGenTimer = m_Metrics.ObserveCardGenerationDuration("genshin charlist");
        m_Logger.LogInformation(LogMessage.CardGenStartInfo, "CharList", context.UserId);

        var charData = context.Data.Item1.ToList();
        var buddyData = context.Data.Item2.ToList();
        List<IDisposable> disposables = [];

        try
        {
            m_Logger.LogInformation("Generating character list card for user {UserId} with {CharCount} characters",
                context.GameProfile.GameUid, charData.Count);

            var avatarImages = await charData
                .OrderByDescending(x => x.Level)
                .ThenByDescending(x => x.Rarity)
                .ThenBy(x => x.Name)
                .ToAsyncEnumerable()
                .Select(async (x, token) =>
                {
                    var image = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(x.ToImageName(), token), token);
                    using ZzzAvatar avatar = new(x.Id, x.Level, x.Rarity[0], x.Rank, image);
                    return avatar.GetStyledAvatarImage();
                }).ToListAsync();
            disposables.AddRange(avatarImages);

            var buddyImages = await buddyData
                .OrderByDescending(x => x.Level)
                .ThenByDescending(x => x.Star)
                .ThenBy(x => x.Name)
                .ToAsyncEnumerable()
                .Select(async (x, token) =>
                {
                    using var image = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(x!.ToImageName(), token), token);
                    return x.GetStyledBuddyImage(image, m_StarImages[x.Star]);
                })
                .ToListAsync();
            disposables.AddRange(buddyImages);

            var layout = ImageUtility.CalculateSplitGridLayout(avatarImages.Count, buddyImages.Count,
                150, 180, [120, 50, 50, 50], 20, 80);

            var charCountByElem = charData
                .GroupBy(x => x.ElementType)
                .OrderBy(x => x.Key)
                .Select(x => new { Element = ToTitleCase(StatUtils.GetElementNameFromId(x.Key, 0)), Count = x.Count() })
                .ToList();

            var charCountByProfession = charData
                .GroupBy(x => x.AvatarProfession)
                .OrderBy(x => x.Key)
                .Select(x => new { Profession = StatUtils.GetProfessionNameFromId(x.Key), Count = x.Count() })
                .ToList();

            var charCountByRarity = charData
                .GroupBy(x => x.Rarity.ToUpperInvariant())
                .OrderBy(x => RarityOrder.IndexOf(x.Key[0]))
                .Select(x => new { Rarity = $"{x.Key}-Rank", Count = x.Count() })
                .ToList();

            using Image<Rgba32> background = new(layout.OutputWidth, layout.OutputHeight + 180);
            background.Mutate(ctx =>
            {
                ctx.Clear(Color.FromRgb(69, 69, 69));

                ctx.DrawText(new RichTextOptions(m_TitleFont)
                {
                    Origin = new Vector2(50, 80),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, $"{context.GameProfile.Nickname}·IK {context.GameProfile.Level}", Color.White);

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(50, 110),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, context.GameProfile.GameUid!, Color.White);

                var i = 0;

                while (i < avatarImages.Count)
                {
                    var pos = layout.ImagePositions[i];
                    ctx.DrawImage(avatarImages[i], new Point(pos.X, pos.Y), 1f);
                    i++;
                }

                i = 0;
                while (i < buddyImages.Count)
                {
                    var pos = layout.ImagePositions[avatarImages.Count + i];
                    ctx.DrawImage(buddyImages[i], new Point(pos.X, pos.Y), 1f);
                    i++;
                }

                var yOffset = layout.OutputHeight - 25;
                var xOffset = 50;

                foreach (var entry in charCountByRarity)
                {
                    var countSize = TextMeasurer.MeasureSize(entry.Count.ToString(),
                        new TextOptions(m_NormalFont));
                    var elemSize =
                        TextMeasurer.MeasureSize(entry.Rarity, new TextOptions(m_NormalFont));
                    FontRectangle size = new(0, 0, countSize.Width + elemSize.Width + 20,
                        countSize.Height + elemSize.Height);
                    var overlay =
                        ImageUtility.CreateRoundedRectanglePath((int)size.Width + 50, 50, 10)
                            .Translate(xOffset, yOffset);
                    EllipsePolygon foreground = new(new PointF(xOffset + 20, yOffset + 25), 10);
                    ctx.Fill(entry.Rarity == "S-Rank" ? GoldBackgroundColor.WithAlpha(128) : PurpleBackgroundColor.WithAlpha(128), overlay);
                    ctx.Fill(entry.Rarity == "S-Rank" ? Color.Gold : PurpleForegroundColor, foreground);
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(xOffset + 40, yOffset + 32),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Center
                    }, entry.Rarity, Color.White);
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(xOffset + 35 + size.Width, yOffset + 32),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center
                    }, entry.Count.ToString(), Color.White);
                    xOffset += (int)size.Width + 70;
                }

                yOffset += 60;
                xOffset = 50;

                foreach (var entry in charCountByElem)
                {
                    var countSize = TextMeasurer.MeasureSize(entry.Count.ToString(),
                        new TextOptions(m_NormalFont));
                    var elemSize = TextMeasurer.MeasureSize(entry.Element, new TextOptions(m_NormalFont));
                    FontRectangle size = new(0, 0, countSize.Width + elemSize.Width + 20,
                        countSize.Height + elemSize.Height);
                    var overlay =
                        ImageUtility.CreateRoundedRectanglePath((int)size.Width + 50, 50, 10)
                            .Translate(xOffset, yOffset);
                    EllipsePolygon foreground = new(new PointF(xOffset + 20, yOffset + 25), 10);
                    ctx.Fill(ElementBackground[entry.Element], overlay);
                    ctx.Fill(ElementForeground[entry.Element], foreground);
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(xOffset + 40, yOffset + 32),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Center
                    }, entry.Element, Color.White);
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(xOffset + 35 + size.Width, yOffset + 32),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center
                    }, entry.Count.ToString(), Color.White);
                    xOffset += (int)size.Width + 70;
                }

                yOffset += 60;
                xOffset = 50;

                foreach (var entry in charCountByProfession)
                {
                    var countSize = TextMeasurer.MeasureSize(entry.Count.ToString(),
                        new TextOptions(m_NormalFont));
                    var elemSize = TextMeasurer.MeasureSize(entry.Profession, new TextOptions(m_NormalFont));
                    FontRectangle size = new(0, 0, countSize.Width + elemSize.Width + 20,
                        countSize.Height + elemSize.Height);
                    var overlay =
                        ImageUtility.CreateRoundedRectanglePath((int)size.Width + 50, 50, 10)
                            .Translate(xOffset, yOffset);
                    EllipsePolygon foreground = new(new PointF(xOffset + 20, yOffset + 25), 10);
                    ctx.Fill(Color.FromRgb(24, 24, 24), overlay);
                    ctx.Fill(Color.White, foreground);
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(xOffset + 40, yOffset + 32),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Center
                    }, entry.Profession, Color.White);
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(xOffset + 35 + size.Width, yOffset + 32),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center
                    }, entry.Count.ToString(), Color.White);
                    xOffset += (int)size.Width + 70;
                }
            });

            MemoryStream stream = new();
            await background.SaveAsJpegAsync(stream, JpegEncoder);
            stream.Position = 0;
            return stream;
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, LogMessage.CardGenError, "CharList", context.UserId,
                JsonSerializer.Serialize(context.Data));
            throw new CommandException("Failed to generate CharList card", ex);
        }
        finally
        {
            foreach (var disposable in disposables) disposable.Dispose();
        }
    }

    private static string ToTitleCase(string str)
    {
        return TextInfo.ToTitleCase(str.ToLowerInvariant());
    }
}
