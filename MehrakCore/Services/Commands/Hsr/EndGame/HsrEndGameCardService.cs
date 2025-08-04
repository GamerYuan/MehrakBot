#region

using System.Numerics;
using System.Text.Json;
using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Models;
using MehrakCore.Repositories;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace MehrakCore.Services.Commands.Hsr.EndGame;

internal class HsrEndGameCardService : ICommandService<BaseHsrEndGameCommandExecutor>
{
    private readonly ImageRepository m_ImageRepository;
    private readonly ILogger<HsrEndGameCardService> m_Logger;
    private readonly Font m_TitleFont;
    private readonly Font m_NormalFont;

    private readonly Image m_StarLit;
    private readonly Image m_StarUnlit;
    private readonly Image m_CycleIcon;
    private readonly Image m_PfBackground;

    private static readonly JpegEncoder JpegEncoder = new()
    {
        Quality = 90,
        Interleaved = false
    };

    private static readonly Color OverlayColor = Color.FromRgba(0, 0, 0, 128);

    public HsrEndGameCardService(ImageRepository imageRepository, ILogger<HsrEndGameCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_Logger = logger;

        var collection = new FontCollection();
        var fontFamily = collection.Add("Assets/Fonts/hsr.ttf");

        m_TitleFont = fontFamily.CreateFont(40, FontStyle.Bold);
        m_NormalFont = fontFamily.CreateFont(28, FontStyle.Regular);

        m_StarLit = Image.Load(m_ImageRepository.DownloadFileToStreamAsync("hsr_moc_star").Result);
        m_StarUnlit = m_StarLit.CloneAs<Rgba32>();
        m_StarUnlit.Mutate(ctx =>
        {
            ctx.Grayscale();
            ctx.Brightness(0.7f);
        });

        m_PfBackground = Image.Load(m_ImageRepository.DownloadFileToStreamAsync("hsr_pf_bg").Result);
        m_PfBackground.Mutate(ctx => ctx.Brightness(0.5f));

        m_CycleIcon = Image.Load(m_ImageRepository.DownloadFileToStreamAsync("hsr_hourglass").Result);
    }

    public async ValueTask<Stream> GetEndGameCardImageAsync(EndGameMode gameMode, UserGameData gameData,
        HsrEndInformation gameModeData, Dictionary<int, Stream> buffMap)
    {
        List<IDisposable> disposables = [];
        try
        {
            var avatarImageTask = gameModeData.AllFloorDetail.Where(x => x is { Node1: not null, Node2: not null })
                .SelectMany(x => x.Node1!.Avatars.Concat(x.Node2!.Avatars))
                .DistinctBy(x => x.Id).ToAsyncEnumerable().SelectAwait(async x =>
                    await Task.FromResult(new HsrAvatar(x.Id, x.Level, x.Rarity, x.Rank,
                        await Image.LoadAsync(
                            await m_ImageRepository.DownloadFileToStreamAsync($"hsr_avatar_{x.Id}")))))
                .ToDictionaryAwaitAsync(async x => await Task.FromResult(x),
                    async x => await Task.FromResult(x.GetStyledAvatarImage()), HsrAvatarIdComparer.Instance);
            var buffImageTask = buffMap.ToAsyncEnumerable().ToDictionaryAwaitAsync(
                async x => await Task.FromResult(x.Key),
                async x => await Image.LoadAsync(x.Value));

            var avatarImages = await avatarImageTask;
            var buffImages = await buffImageTask;

            disposables.AddRange(avatarImages.Keys);
            disposables.AddRange(avatarImages.Values);
            disposables.AddRange(buffImages.Values);

            var lookup = avatarImages.GetAlternateLookup<int>();
            var floorDetails = Enumerable.Range(0, 4)
                .Select(floorIndex =>
                {
                    var floorData = gameModeData.AllFloorDetail
                        .FirstOrDefault(x => HsrCommandUtility.GetFloorNumber(x.Name) - 1 == floorIndex);
                    return (FloorNumber: floorIndex, Data: floorData);
                })
                .ToList();
            var height = 180 + floorDetails.Chunk(2)
                .Select(x => x.All(y => y.Data == null || IsSmallBlob(y.Data)) ? 200 : 620).Sum();

            var background = gameMode switch
            {
                EndGameMode.PureFiction => m_PfBackground.CloneAs<Rgba32>(),
                EndGameMode.ApocalypticShadow => new Image<Rgba32>(1950, height),
                _ => throw new ArgumentOutOfRangeException(nameof(gameMode), gameMode, null)
            };

            disposables.Add(background);

            background.Mutate(ctx =>
            {
                ctx.Resize(new ResizeOptions
                {
                    CenterCoordinates = new PointF(ctx.GetCurrentSize().Width / 2f, ctx.GetCurrentSize().Height / 2f),
                    Size = new Size(1950, height),
                    Mode = ResizeMode.Crop,
                    Sampler = KnownResamplers.Bicubic
                    // TargetRectangle = new Rectangle(0, 0, 1950, height)
                });

                var group = gameModeData.Groups.First();
                ctx.DrawText(new RichTextOptions(m_TitleFont)
                {
                    Origin = new Vector2(50, 80),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, "Pure Fiction", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(50, 120),
                        VerticalAlignment = VerticalAlignment.Bottom
                    },
                    $"{group.BeginTime.Day}/{group.BeginTime.Month}/{group.BeginTime.Year} - " +
                    $"{group.EndTime.Day}/{group.EndTime.Month}/{group.EndTime.Year}",
                    Color.White);
                ctx.DrawLine(Color.White, 3f, new PointF(305, 40), new PointF(305, 80));
                var textOptions = new RichTextOptions(m_TitleFont)
                {
                    Origin = new Vector2(325, 80),
                    VerticalAlignment = VerticalAlignment.Bottom
                };
                var bounds = TextMeasurer.MeasureBounds(gameModeData.StarNum.ToString(), textOptions);
                ctx.DrawText(textOptions, gameModeData.StarNum.ToString(), Color.White);
                ctx.DrawImage(m_StarLit, new Point((int)bounds.Right + 5, 30), 1f);

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(1900, 80),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Bottom
                    },
                    $"{gameData.Nickname} • TB {gameData.Level}", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(1900, 120),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom
                }, gameData.GameUid!, Color.White);

                var yOffset = 150;
                foreach (var (floorNumber, floorData) in floorDetails)
                {
                    var xOffset = floorNumber % 2 * 950 + 50;

                    IPath overlay;

                    if (floorData == null || floorData.IsFast)
                    {
                        if ((floorNumber % 2 == 0 && floorNumber + 1 < floorDetails.Count &&
                             !IsSmallBlob(floorDetails[floorNumber + 1].Data)) ||
                            (floorNumber % 2 == 1 && floorNumber - 1 >= 0 &&
                             !IsSmallBlob(floorDetails[floorNumber - 1].Data)))
                        {
                            overlay = ImageUtility.CreateRoundedRectanglePath(900, 600, 15)
                                .Translate(xOffset, yOffset);
                            ctx.Fill(OverlayColor, overlay);
                            ctx.DrawText(new RichTextOptions(m_NormalFont)
                            {
                                Origin = new Vector2(xOffset + 450, yOffset + 280),
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center
                            }, floorData?.IsFast ?? false ? "Quick Clear" : "No Clear Records", Color.White);
                        }
                        else
                        {
                            overlay = ImageUtility.CreateRoundedRectanglePath(900, 180, 15)
                                .Translate(xOffset, yOffset);
                            ctx.Fill(OverlayColor, overlay);
                            ctx.DrawText(new RichTextOptions(m_NormalFont)
                            {
                                Origin = new Vector2(xOffset + 450, yOffset + 110),
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center
                            }, floorData?.IsFast ?? false ? "Quick Clear" : "No Clear Records", Color.White);
                        }

                        ctx.DrawText(new RichTextOptions(m_NormalFont)
                            {
                                Origin = new Vector2(xOffset + 20, yOffset + 20),
                                HorizontalAlignment = HorizontalAlignment.Left,
                                VerticalAlignment = VerticalAlignment.Top
                            },
                            floorData?.Name ??
                            $"{gameModeData.Groups.First().Name} ({HsrCommandUtility.GetRomanNumeral(floorNumber)})",
                            Color.White);

                        for (int i = 0; i < 3; i++)
                            ctx.DrawImage(i < (floorData?.StarNum ?? 0) ? m_StarLit : m_StarUnlit,
                                new Point(xOffset + 730 + i * 50, yOffset + 5), 1f);

                        if (floorNumber % 2 == 1) yOffset += 200;
                        continue;
                    }

                    overlay = ImageUtility.CreateRoundedRectanglePath(900, 600, 15).Translate(xOffset, yOffset);
                    ctx.Fill(OverlayColor, overlay);
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(xOffset + 20, yOffset + 20),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top
                    }, floorData.Name, Color.White);

                    using var node1 = GetRosterImage(floorData.Node1!.Avatars.Select(x => x.Id).ToList(), lookup);
                    using var node2 = GetRosterImage(floorData.Node2!.Avatars.Select(x => x.Id).ToList(), lookup);
                    disposables.AddRange(node1, node2);
                    ctx.DrawLine(Color.White, 2f, new PointF(xOffset + 20, yOffset + 65),
                        new PointF(xOffset + 880, yOffset + 65));
                    ctx.DrawText("Node 1", m_NormalFont, Color.White, new PointF(xOffset + 45, yOffset + 85));
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                        {
                            Origin = new Vector2(xOffset + 855, yOffset + 85),
                            HorizontalAlignment = HorizontalAlignment.Right
                        }, $"Score: {floorData.Node1.Score}", Color.White);
                    ctx.DrawImage(node1, new Point(xOffset + 55, yOffset + 130), 1f);
                    IPath ellipse = new EllipsePolygon(new PointF(xOffset + 780, yOffset + 220), 55);
                    ctx.Fill(Color.Black, ellipse);
                    ctx.Draw(Color.White, 1f, ellipse);
                    ctx.DrawImage(buffImages[floorData.Node1.Buff.Id], new Point(xOffset + 715, yOffset + 160),
                        1f);
                    ctx.DrawLine(Color.White, 2f, new PointF(xOffset + 40, yOffset + 335),
                        new PointF(xOffset + 860, yOffset + 335));
                    ctx.DrawText("Node 2", m_NormalFont, Color.White, new PointF(xOffset + 45, yOffset + 350));
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                        {
                            Origin = new Vector2(xOffset + 855, yOffset + 350),
                            HorizontalAlignment = HorizontalAlignment.Right
                        }, $"Score: {floorData.Node2.Score}", Color.White);
                    ctx.DrawImage(node2, new Point(xOffset + 55, yOffset + 395), 1f);
                    ellipse = ellipse.Translate(0, 265);
                    ctx.Fill(Color.Black, ellipse);
                    ctx.Draw(Color.White, 1f, ellipse);
                    ctx.DrawImage(buffImages[floorData.Node2.Buff.Id], new Point(xOffset + 715, yOffset + 425),
                        1f);

                    for (int i = 0; i < 3; i++)
                        ctx.DrawImage(i < floorData.StarNum ? m_StarLit : m_StarUnlit,
                            new Point(xOffset + 730 + i * 50, yOffset + 5), 1f);
                    ctx.DrawLine(Color.White, 2f, new PointF(xOffset + 720, yOffset + 10),
                        new PointF(xOffset + 720, yOffset + 55));
                    var scoreText = $"Score: {int.Parse(floorData.Node1.Score) + int.Parse(floorData.Node2.Score)}";
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                        {
                            Origin = new Vector2(xOffset + 710, yOffset + 20),
                            HorizontalAlignment = HorizontalAlignment.Right,
                            VerticalAlignment = VerticalAlignment.Top
                        }, scoreText,
                        Color.White);

                    // Draw cycle number
                    if (gameMode == EndGameMode.PureFiction)
                    {
                        var size = TextMeasurer.MeasureSize(scoreText, new TextOptions(m_NormalFont));
                        ctx.DrawLine(Color.White, 2f, new PointF(xOffset + 695 - (int)size.Width, yOffset + 10),
                            new PointF(xOffset + 695 - (int)size.Width, yOffset + 55));

                        ctx.DrawText(new RichTextOptions(m_NormalFont)
                        {
                            Origin = new Vector2(xOffset + 650 - (int)size.Width, yOffset + 20),
                            HorizontalAlignment = HorizontalAlignment.Right,
                            VerticalAlignment = VerticalAlignment.Top
                        }, floorData.RoundNum.ToString(), Color.White);
                        ctx.DrawImage(m_CycleIcon, new Point(xOffset + 650 - (int)size.Width, yOffset + 10), 1f);
                        if (floorNumber % 2 == 1) yOffset += 620;
                    }
                }
            });

            MemoryStream stream = new();
            await background.SaveAsJpegAsync(stream, JpegEncoder);
            stream.Position = 0;
            return stream;
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Failed to generate Pure Fiction card image for uid {UserId}\n{JsonString}",
                gameData, JsonSerializer.Serialize(gameModeData));
            throw new CommandException("An error occurred while generating Pure Fiction card image", e);
        }
        finally
        {
            disposables.ForEach(x => x.Dispose());
        }
    }

    private static bool IsSmallBlob(HsrEndFloorDetail? data)
    {
        return data == null || data.IsFast;
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
}
