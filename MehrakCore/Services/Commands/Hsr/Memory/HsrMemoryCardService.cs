#region

using System.Numerics;
using System.Text.Json;
using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Models;
using MehrakCore.Repositories;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ImageExtensions = MehrakCore.Utility.ImageExtensions;

#endregion

namespace MehrakCore.Services.Commands.Hsr.Memory;

internal class HsrMemoryCardService : ICommandService<HsrMemoryCommandExecutor>
{
    private readonly ImageRepository m_ImageRepository;
    private readonly ILogger<HsrMemoryCardService> m_Logger;

    private static readonly JpegEncoder JpegEncoder = new()
    {
        Interleaved = false,
        Quality = 90,
        ColorType = JpegEncodingColor.Rgb
    };

    private static readonly Color OverlayColor = Color.FromRgba(0, 0, 0, 128);

    private readonly Image m_StarLit;
    private readonly Image m_StarUnlit;
    private readonly Image m_CycleIcon;
    private readonly Image m_Background;

    private readonly Font m_TitleFont;
    private readonly Font m_NormalFont;

    public HsrMemoryCardService(ImageRepository imageRepository, ILogger<HsrMemoryCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_Logger = logger;

        var collection = new FontCollection();
        var fontFamily = collection.Add("Assets/Fonts/hsr.ttf");

        m_TitleFont = fontFamily.CreateFont(40, FontStyle.Bold);
        m_NormalFont = fontFamily.CreateFont(28, FontStyle.Regular);

        m_StarLit = Image.Load(m_ImageRepository.DownloadFileToStreamAsync("hsr_moc_star").Result);
        m_StarUnlit = m_StarLit.CloneAs<Rgba32>();
        m_StarUnlit.Mutate(ctx => ctx.Grayscale());
        m_CycleIcon = Image.Load(m_ImageRepository.DownloadFileToStreamAsync("hsr_hourglass").Result);

        m_Background = Image.Load(m_ImageRepository.DownloadFileToStreamAsync("hsr_moc_bg").Result);
        m_Background.Mutate(ctx =>
        {
            ctx.Brightness(0.5f);
            ctx.GaussianBlur(5);
        });
    }

    public async ValueTask<Stream> GetMemoryCardImageAsync(UserGameData gameData, HsrMemoryInformation memoryData)
    {
        List<IDisposable> disposableResources = [];
        try
        {
            var avatarImages = await memoryData.AllFloorDetail!.SelectMany(x => x.Node1.Avatars.Concat(x.Node2.Avatars))
                .DistinctBy(x => x.Id)
                .ToAsyncEnumerable()
                .SelectAwait(async x => new HsrAvatar(x.Id, x.Level, x.Rarity, x.Rank,
                    await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync($"hsr_avatar_{x.Id}"))))
                .ToDictionaryAwaitAsync(async x => await Task.FromResult(x),
                    async x => await Task.FromResult(x.GetStyledAvatarImage()),
                    HsrAvatarIdComparer.Instance);

            var lookup = avatarImages.GetAlternateLookup<int>();
            var floorDetails = memoryData.AllFloorDetail!
                .Select(x => (FloorNumber: GetFloorNumber(x.Name) - 1, Data: x))
                .OrderBy(x => x.FloorNumber).ToList();
            var height = 180 + floorDetails.Chunk(2)
                .Select(x => x.All(y => IsSmallBlob(y.Data)) ? 200 : 500).Sum();

            disposableResources.AddRange(avatarImages.Keys);
            disposableResources.AddRange(avatarImages.Values);

            var background = m_Background.CloneAs<Rgba32>();
            disposableResources.Add(background);

            background.Mutate(ctx =>
            {
                ctx.Resize(0, height, KnownResamplers.Bicubic);
                ctx.Crop(new Rectangle((ctx.GetCurrentSize().Width - 1550) / 2, 0, 1550, height));

                ctx.DrawText(new RichTextOptions(m_TitleFont)
                {
                    Origin = new Vector2(50, 80),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, "Memory of Chaos", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(50, 110),
                        VerticalAlignment = VerticalAlignment.Bottom
                    },
                    $"{memoryData.StartTime.Day}/{memoryData.StartTime.Month}/{memoryData.StartTime.Year} - " +
                    $"{memoryData.EndTime.Day}/{memoryData.EndTime.Month}/{memoryData.EndTime.Year}",
                    Color.White);
                ctx.DrawLine(Color.White, 3f, new PointF(415, 40), new PointF(415, 80));
                var textOptions = new RichTextOptions(m_TitleFont)
                {
                    Origin = new Vector2(435, 80),
                    VerticalAlignment = VerticalAlignment.Bottom
                };
                var bounds = TextMeasurer.MeasureBounds(memoryData.StarNum.ToString(), textOptions);
                ctx.DrawText(textOptions, memoryData.StarNum.ToString(), Color.White);
                ctx.DrawImage(m_StarLit, new Point((int)bounds.Right + 5, 30), 1f);

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(1500, 80),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Bottom
                    },
                    $"{gameData.Nickname} • TB {gameData.Level}", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(1500, 110),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom
                }, gameData.GameUid!, Color.White);

                var yOffset = 150;
                foreach (var (floorNumber, floorData) in floorDetails)
                {
                    var xOffset = floorNumber % 2 * 750 + 50;

                    IPath overlay;

                    if (floorData.IsFast || floorData.Node1.Avatars.Count == 0)
                    {
                        if ((floorNumber % 2 == 0 && floorNumber + 1 < floorDetails.Count &&
                             !IsSmallBlob(floorDetails[floorNumber + 1].Data)) ||
                            (floorNumber % 2 == 1 && floorNumber - 1 >= 0 &&
                             !IsSmallBlob(floorDetails[floorNumber - 1].Data)))
                        {
                            overlay = ImageExtensions.CreateRoundedRectanglePath(700, 480, 15)
                                .Translate(xOffset, yOffset);
                            ctx.Fill(OverlayColor, overlay);
                            ctx.DrawText(new RichTextOptions(m_NormalFont)
                            {
                                Origin = new Vector2(xOffset + 350, yOffset + 260),
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center
                            }, floorData.IsFast ? "Quick Clear" : "No Clear Records", Color.White);
                        }
                        else
                        {
                            overlay = ImageExtensions.CreateRoundedRectanglePath(700, 180, 15)
                                .Translate(xOffset, yOffset);
                            ctx.Fill(OverlayColor, overlay);
                            ctx.DrawText(new RichTextOptions(m_NormalFont)
                            {
                                Origin = new Vector2(xOffset + 350, yOffset + 110),
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center
                            }, floorData.IsFast ? "Quick Clear" : "No Clear Records", Color.White);
                        }

                        ctx.DrawText(new RichTextOptions(m_NormalFont)
                        {
                            Origin = new Vector2(xOffset + 20, yOffset + 20),
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Top
                        }, floorData.Name, Color.White);

                        for (int i = 0; i < 3; i++)
                            ctx.DrawImage(i < floorData.StarNum ? m_StarLit : m_StarUnlit,
                                new Point(xOffset + 530 + i * 50, yOffset + 5), 1f);

                        if (floorNumber % 2 == 1) yOffset += 200;
                        continue;
                    }

                    overlay = ImageExtensions.CreateRoundedRectanglePath(700, 480, 15).Translate(xOffset, yOffset);
                    ctx.Fill(OverlayColor, overlay);
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(xOffset + 20, yOffset + 20),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top
                    }, floorData.Name, Color.White);

                    using var node1 = GetRosterImage(floorData.Node1.Avatars.Select(x => x.Id).ToList(), lookup);
                    using var node2 = GetRosterImage(floorData.Node2.Avatars.Select(x => x.Id).ToList(), lookup);
                    disposableResources.AddRange(node1, node2);
                    ctx.DrawImage(node1, new Point(xOffset + 25, yOffset + 65), 1f);
                    ctx.DrawLine(Color.White, 2f, new PointF(xOffset + 20, yOffset + 260),
                        new PointF(xOffset + 680, yOffset + 260));
                    ctx.DrawImage(node2, new Point(xOffset + 25, yOffset + 275), 1f);
                    for (int i = 0; i < 3; i++)
                        ctx.DrawImage(i < floorData.StarNum ? m_StarLit : m_StarUnlit,
                            new Point(xOffset + 530 + i * 50, yOffset + 5), 1f);
                    ctx.DrawLine(Color.White, 2f, new PointF(xOffset + 520, yOffset + 10),
                        new PointF(xOffset + 520, yOffset + 55));
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(xOffset + 470, yOffset + 20),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Top
                    }, floorData.RoundNum.ToString(), Color.White);
                    ctx.DrawImage(m_CycleIcon, new Point(xOffset + 470, yOffset + 10), 1f);
                    if (floorNumber % 2 == 1) yOffset += 500;
                }
            });

            MemoryStream stream = new();
            await background.SaveAsJpegAsync(stream, JpegEncoder);
            stream.Position = 0;
            return stream;
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Failed to generate memory card image for uid {UserId}\n{JsonString}",
                gameData, JsonSerializer.Serialize(memoryData));
            throw new CommandException("An error occurred while generating Memory of Chaos card image", e);
        }
        finally
        {
            disposableResources.ForEach(x => x.Dispose());
        }
    }

    private static Image<Rgba32> GetRosterImage(List<int> avatarIds,
        Dictionary<HsrAvatar, Image<Rgba32>>.AlternateLookup<int> imageDict)
    {
        const int avatarWidth = 150;

        int offset = (4 - avatarIds.Count) * avatarWidth / 2 + 10;

        var rosterImage = new Image<Rgba32>(650, 200);

        rosterImage.Mutate(ctx =>
        {
            ctx.Clear(Color.Transparent);

            for (int i = 0; i < avatarIds.Count; i++)
            {
                int x = offset + i * (avatarWidth + 10);
                ctx.DrawImage(imageDict[avatarIds[i]], new Point(x, 0), 1f);
            }
        });

        return rosterImage;
    }

    private static int GetFloorNumber(string text)
    {
        var startIndex = text.LastIndexOf('(');
        var endIndex = text.LastIndexOf(')');

        if (startIndex == -1 || endIndex == -1 || startIndex >= endIndex) return 0; // Or handle as an error

        string roman = text.Substring(startIndex + 1, endIndex - startIndex - 1).ToUpper();

        if (string.IsNullOrEmpty(roman)) return 0;

        var romanMap = new Dictionary<char, int>
        {
            { 'I', 1 },
            { 'V', 5 },
            { 'X', 10 },
            { 'L', 50 },
            { 'C', 100 },
            { 'D', 500 },
            { 'M', 1000 }
        };

        int total = 0;
        int prevValue = 0;

        for (int i = roman.Length - 1; i >= 0; i--)
        {
            if (!romanMap.TryGetValue(roman[i], out var currentValue)) return 0; // Invalid character

            if (currentValue < prevValue)
                total -= currentValue;
            else
                total += currentValue;
            prevValue = currentValue;
        }

        return total;
    }

    private static bool IsSmallBlob(FloorDetail floor)
    {
        return floor.IsFast || floor.Node1.Avatars.Count == 0;
    }
}
