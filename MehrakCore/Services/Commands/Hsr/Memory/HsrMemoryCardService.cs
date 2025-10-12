#region

using Mehrak.Application.Models;
using Mehrak.Application.Utility;
using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Constants;
using MehrakCore.Models;
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

namespace MehrakCore.Services.Commands.Hsr.Memory;

internal class HsrMemoryCardService : ICommandService<HsrMemoryCommandExecutor>, IAsyncInitializable
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

    private Image m_StarLit = null!;
    private Image m_StarUnlit = null!;
    private Image m_CycleIcon = null!;
    private Image m_Background = null!;

    private readonly Font m_TitleFont;
    private readonly Font m_NormalFont;

    public HsrMemoryCardService(ImageRepository imageRepository, ILogger<HsrMemoryCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_Logger = logger;

        FontCollection collection = new();
        FontFamily fontFamily = collection.Add("Assets/Fonts/hsr.ttf");

        m_TitleFont = fontFamily.CreateFont(40, FontStyle.Bold);
        m_NormalFont = fontFamily.CreateFont(28, FontStyle.Regular);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        m_StarLit = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("hsr_moc_star"), cancellationToken);
        m_StarUnlit = m_StarLit.CloneAs<Rgba32>();
        m_StarUnlit.Mutate(ctx =>
        {
            ctx.Grayscale();
            ctx.Brightness(0.7f);
        });
        m_CycleIcon = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("hsr_hourglass"), cancellationToken);

        m_Background = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("hsr_moc_bg"), cancellationToken);
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
            Dictionary<HsrAvatar, Image<Rgba32>> avatarImages = await memoryData.AllFloorDetail!.SelectMany(x => x.Node1.Avatars.Concat(x.Node2.Avatars))
                .DistinctBy(x => x.Id)
                .ToAsyncEnumerable()
                .SelectAwait(async x => new HsrAvatar(x.Id, x.Level, x.Rarity, x.Rank,
                    await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.HsrAvatarName, x.Id)))))
                .ToDictionaryAwaitAsync(async x => await Task.FromResult(x),
                    async x => await Task.FromResult(x.GetStyledAvatarImage()),
                    HsrAvatarIdComparer.Instance);

            Dictionary<HsrAvatar, Image<Rgba32>>.AlternateLookup<int> lookup = avatarImages.GetAlternateLookup<int>();
            List<(int FloorNumber, FloorDetail? Data)> floorDetails = [.. Enumerable.Range(0, 12)
                .Select(floorIndex =>
                {
                    FloorDetail? floorData = memoryData.AllFloorDetail!
                        .FirstOrDefault(x => HsrCommandUtility.GetFloorNumber(x.Name) - 1 == floorIndex);
                    return (FloorNumber: floorIndex, Data: floorData);
                })];
            int height = 180 + floorDetails.Chunk(2)
                .Select(x => x.All(y => y.Data == null || IsSmallBlob(y.Data)) ? 200 : 520).Sum();

            disposableResources.AddRange(avatarImages.Keys);
            disposableResources.AddRange(avatarImages.Values);

            Image<Rgba32> background = m_Background.CloneAs<Rgba32>();
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
                RichTextOptions textOptions = new(m_TitleFont)
                {
                    Origin = new Vector2(435, 80),
                    VerticalAlignment = VerticalAlignment.Bottom
                };
                FontRectangle bounds = TextMeasurer.MeasureBounds(memoryData.StarNum.ToString(), textOptions);
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

                int yOffset = 150;
                foreach ((int floorNumber, FloorDetail? floorData) in floorDetails)
                {
                    int xOffset = floorNumber % 2 * 750 + 50;

                    IPath overlay;

                    if (floorData == null || floorData.IsFast)
                    {
                        if ((floorNumber % 2 == 0 && floorNumber + 1 < floorDetails.Count &&
                             !IsSmallBlob(floorDetails[floorNumber + 1].Data)) ||
                            (floorNumber % 2 == 1 && floorNumber - 1 >= 0 &&
                             !IsSmallBlob(floorDetails[floorNumber - 1].Data)))
                        {
                            overlay = ImageUtility.CreateRoundedRectanglePath(700, 500, 15)
                                .Translate(xOffset, yOffset);
                            ctx.Fill(OverlayColor, overlay);
                            ctx.DrawText(new RichTextOptions(m_NormalFont)
                            {
                                Origin = new Vector2(xOffset + 350, yOffset + 280),
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center
                            }, floorData?.IsFast ?? false ? "Quick Clear" : "No Clear Records", Color.White);
                        }
                        else
                        {
                            overlay = ImageUtility.CreateRoundedRectanglePath(700, 180, 15)
                                .Translate(xOffset, yOffset);
                            ctx.Fill(OverlayColor, overlay);
                            ctx.DrawText(new RichTextOptions(m_NormalFont)
                            {
                                Origin = new Vector2(xOffset + 350, yOffset + 110),
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center
                            }, floorData?.IsFast ?? false ? "Quick Clear" : "No Clear Records", Color.White);
                        }

                        string stageText =
                            $"{memoryData.Groups[0].Name} ({HsrCommandUtility.GetRomanNumeral(floorNumber + 1)})";
                        ctx.DrawText(new RichTextOptions(m_NormalFont)
                        {
                            Origin = new Vector2(xOffset + 20, yOffset + 20),
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Top
                        }, floorData?.Name ?? stageText, Color.White);

                        for (int i = 0; i < 3; i++)
                            ctx.DrawImage(i < (floorData?.StarNum ?? 0) ? m_StarLit : m_StarUnlit,
                                new Point(xOffset + 530 + i * 50, yOffset + 5), 1f);

                        if (floorNumber % 2 == 1) yOffset += 200;
                        continue;
                    }

                    overlay = ImageUtility.CreateRoundedRectanglePath(700, 500, 15).Translate(xOffset, yOffset);
                    ctx.Fill(OverlayColor, overlay);
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(xOffset + 20, yOffset + 20),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top
                    }, floorData.Name, Color.White);

                    using Image<Rgba32> node1 = GetRosterImage([.. floorData.Node1.Avatars.Select(x => x.Id)], lookup);
                    using Image<Rgba32> node2 = GetRosterImage([.. floorData.Node2.Avatars.Select(x => x.Id)], lookup);
                    disposableResources.AddRange(node1, node2);
                    ctx.DrawImage(node1, new Point(xOffset + 25, yOffset + 65), 1f);
                    ctx.DrawLine(Color.White, 2f, new PointF(xOffset + 20, yOffset + 270),
                        new PointF(xOffset + 680, yOffset + 270));
                    ctx.DrawImage(node2, new Point(xOffset + 25, yOffset + 295), 1f);
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
                    if (floorNumber % 2 == 1) yOffset += 520;
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

        Image<Rgba32> rosterImage = new(650, 200);

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

    private static bool IsSmallBlob(FloorDetail? floor)
    {
        return floor == null || floor.IsFast;
    }
}
