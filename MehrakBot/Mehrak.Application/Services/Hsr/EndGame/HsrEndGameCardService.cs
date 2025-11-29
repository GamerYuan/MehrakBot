#region

using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Mehrak.Application.Models;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
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

#endregion

namespace Mehrak.Application.Services.Hsr.EndGame;

internal class HsrEndGameCardService : ICardService<HsrEndInformation>, IAsyncInitializable
{
    private readonly IImageRepository m_ImageRepository;
    private readonly ILogger<HsrEndGameCardService> m_Logger;
    private readonly Font m_TitleFont;
    private readonly Font m_NormalFont;

    private Image m_StarLit = null!;
    private Image m_StarUnlit = null!;
    private Image m_CycleIcon = null!;
    private Image m_PfBackground = null!;
    private Image m_AsBackground = null!;
    private Image m_BossCheckmark = null!;

    private static readonly JpegEncoder JpegEncoder = new()
    {
        Quality = 90,
        Interleaved = false
    };

    private static readonly Color OverlayColor = Color.FromRgba(0, 0, 0, 128);

    public HsrEndGameCardService(IImageRepository imageRepository, ILogger<HsrEndGameCardService> logger)
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
        m_StarLit = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("hsr_moc_star"),
            cancellationToken);
        m_StarUnlit = m_StarLit.CloneAs<Rgba32>();
        m_StarUnlit.Mutate(ctx =>
        {
            ctx.Grayscale();
            ctx.Brightness(0.7f);
        });

        m_PfBackground = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("hsr_pf_bg"),
            cancellationToken);

        m_AsBackground = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("hsr_as_bg"),
            cancellationToken);

        m_CycleIcon = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("hsr_hourglass"),
            cancellationToken);

        m_BossCheckmark = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("hsr_boss_check"),
            cancellationToken);

        m_Logger.LogInformation(LogMessage.ServiceInitialized, nameof(HsrEndGameCardService));
    }

    public async Task<Stream> GetCardAsync(ICardGenerationContext<HsrEndInformation> context)
    {
        var gameMode = context.GetParameter<HsrEndGameMode>("mode");

        m_Logger.LogInformation(LogMessage.CardGenStartInfo, gameMode.GetString(), context.UserId);
        Stopwatch stopwatch = Stopwatch.StartNew();

        var gameModeData = context.Data;
        List<IDisposable> disposables = [];
        try
        {
            var avatarImages = await gameModeData.AllFloorDetail
                .Where(x => x is { Node1: not null, Node2: not null })
                .SelectMany(x => x.Node1!.Avatars.Concat(x.Node2!.Avatars))
                .DistinctBy(x => x.Id).ToAsyncEnumerable().Select(async (x, token) =>
                    await Task.FromResult(new HsrAvatar(x.Id, x.Level, x.Rarity, x.Rank,
                        await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(x.ToImageName()), token))))
                .ToDictionaryAsync(x => x,
                    x => x.GetStyledAvatarImage(), HsrAvatarIdComparer.Instance);
            var buffImages = await gameModeData.AllFloorDetail
                .Where(x => x is { Node1: not null, Node2: not null })
                .SelectMany(x => new HsrEndBuff[] { x.Node1!.Buff, x.Node2!.Buff })
                .Where(x => x is not null)
                .DistinctBy(x => x.Id).ToAsyncEnumerable().ToDictionaryAsync(
                    async (x, token) => await Task.FromResult(x.Id),
                    async (x, token) =>
                    {
                        Image image =
                            await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(x.ToImageName()), token);
                        image.Mutate(ctx => ctx.Resize(110, 0));
                        return image;
                    });

            disposables.AddRange(avatarImages.Keys);
            disposables.AddRange(avatarImages.Values);
            disposables.AddRange(buffImages.Values);

            Dictionary<HsrAvatar, Image<Rgba32>>.AlternateLookup<int> lookup = avatarImages.GetAlternateLookup<int>();
            List<(int FloorNumber, HsrEndFloorDetail? Data)> floorDetails = gameMode switch
            {
                HsrEndGameMode.PureFiction =>
                [
                    .. Enumerable.Range(0, 4)
                        .Select(floorIndex =>
                        {
                            HsrEndFloorDetail? floorData = gameModeData.AllFloorDetail
                                .FirstOrDefault(x => HsrUtility.GetFloorNumber(x.Name) - 1 == floorIndex);
                            return (FloorNumber: floorIndex, Data: floorData);
                        })
                ],
                HsrEndGameMode.ApocalypticShadow =>
                [
                    .. Enumerable.Range(0, 4)
                        .Select(floorIndex =>
                        {
                            HsrEndFloorDetail? floorData = gameModeData.AllFloorDetail
                                .FirstOrDefault(x => x.Name.EndsWith((floorIndex + 1).ToString()));
                            return (FloorNumber: floorIndex, Data: floorData);
                        })
                ],
                _ => throw new InvalidOperationException()
            };
            int height = 180 + floorDetails.Chunk(2)
                .Select(x => x.All(y => y.Data == null || IsSmallBlob(y.Data)) ? 200 : 620).Sum();

            Image<Rgba32> background = gameMode switch
            {
                HsrEndGameMode.PureFiction => m_PfBackground.CloneAs<Rgba32>(),
                HsrEndGameMode.ApocalypticShadow => m_AsBackground.CloneAs<Rgba32>(),
                _ => throw new InvalidOperationException()
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
                });

                HsrEndGroup group = gameModeData.Groups[0];
                string modeString = gameMode.GetString();
                ctx.DrawText(new RichTextOptions(m_TitleFont)
                {
                    Origin = new Vector2(50, 80),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, modeString, Color.White);
                FontRectangle modeTextBounds = TextMeasurer.MeasureBounds(modeString,
                    new TextOptions(m_TitleFont) { Origin = new Vector2(50, 80) });
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(50, 120),
                    VerticalAlignment = VerticalAlignment.Bottom
                },
                    $"{group.BeginTime.Day}/{group.BeginTime.Month}/{group.BeginTime.Year} - " +
                    $"{group.EndTime.Day}/{group.EndTime.Month}/{group.EndTime.Year}",
                    Color.White);
                ctx.DrawLine(Color.White, 3f, new PointF(modeTextBounds.Right + 15, 40),
                    new PointF(modeTextBounds.Right + 15, 80));
                RichTextOptions textOptions = new(m_TitleFont)
                {
                    Origin = new Vector2(modeTextBounds.Right + 30, 80),
                    VerticalAlignment = VerticalAlignment.Bottom
                };
                FontRectangle bounds = TextMeasurer.MeasureBounds(gameModeData.StarNum.ToString(), textOptions);
                ctx.DrawText(textOptions, gameModeData.StarNum.ToString(), Color.White);
                ctx.DrawImage(m_StarLit, new Point((int)bounds.Right + 5, 30), 1f);

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(1900, 80),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom
                },
                    $"{context.GameProfile.Nickname} • TB {context.GameProfile.Level}", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(1900, 120),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom
                }, context.GameProfile.GameUid!, Color.White);

                int yOffset = 150;
                foreach ((int floorNumber, HsrEndFloorDetail? floorData) in floorDetails)
                {
                    int xOffset = floorNumber % 2 * 950 + 50;

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

                        string stageText = gameMode switch
                        {
                            HsrEndGameMode.PureFiction =>
                                $"{gameModeData.Groups[0].Name} ({HsrUtility.GetRomanNumeral(floorNumber + 1)})",
                            HsrEndGameMode.ApocalypticShadow =>
                                $"{gameModeData.Groups[0].Name}: Difficulty {floorNumber + 1}",
                            _ => ""
                        };

                        ctx.DrawText(new RichTextOptions(m_NormalFont)
                        {
                            Origin = new Vector2(xOffset + 20, yOffset + 20),
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Top
                        },
                            floorData?.Name ?? stageText,
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

                    using Image<Rgba32> node1 = GetRosterImage([.. floorData.Node1!.Avatars.Select(x => x.Id)], lookup);
                    using Image<Rgba32> node2 = GetRosterImage([.. floorData.Node2!.Avatars.Select(x => x.Id)], lookup);
                    disposables.AddRange(node1, node2);
                    ctx.DrawLine(Color.White, 2f, new PointF(xOffset + 20, yOffset + 65),
                        new PointF(xOffset + 880, yOffset + 65));
                    ctx.DrawText("Node 1", m_NormalFont, Color.White, new PointF(xOffset + 45, yOffset + 85));
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(xOffset + 855, yOffset + 85),
                        HorizontalAlignment = HorizontalAlignment.Right
                    }, $"Score: {floorData.Node1.Score}", Color.White);
                    if (gameMode == HsrEndGameMode.ApocalypticShadow && floorData.Node1.BossDefeated)
                        ctx.DrawImage(m_BossCheckmark, new Point(xOffset + 650, yOffset + 83),
                            1f);

                    ctx.DrawImage(node1, new Point(xOffset + 55, yOffset + 130), 1f);
                    IPath ellipse = new EllipsePolygon(new PointF(xOffset + 780, yOffset + 220), 55);
                    ctx.Fill(Color.Black, ellipse);
                    ctx.Draw(Color.White, 1f, ellipse);
                    ctx.DrawImage(buffImages[floorData.Node1.Buff.Id], new Point(xOffset + 725, yOffset + 170),
                        1f);
                    ctx.DrawLine(Color.White, 2f, new PointF(xOffset + 40, yOffset + 335),
                        new PointF(xOffset + 860, yOffset + 335));
                    ctx.DrawText("Node 2", m_NormalFont, Color.White, new PointF(xOffset + 45, yOffset + 350));
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(xOffset + 855, yOffset + 350),
                        HorizontalAlignment = HorizontalAlignment.Right
                    }, $"Score: {floorData.Node2.Score}", Color.White);
                    if (gameMode == HsrEndGameMode.ApocalypticShadow && floorData.Node2.BossDefeated)
                        ctx.DrawImage(m_BossCheckmark, new Point(xOffset + 650, yOffset + 348),
                            1f);

                    ctx.DrawImage(node2, new Point(xOffset + 55, yOffset + 395), 1f);
                    ellipse = ellipse.Translate(0, 265);
                    ctx.Fill(Color.Black, ellipse);
                    ctx.Draw(Color.White, 1f, ellipse);
                    ctx.DrawImage(buffImages[floorData.Node2.Buff.Id], new Point(xOffset + 725, yOffset + 425),
                        1f);

                    for (int i = 0; i < 3; i++)
                        ctx.DrawImage(i < floorData.StarNum ? m_StarLit : m_StarUnlit,
                            new Point(xOffset + 730 + i * 50, yOffset + 5), 1f);
                    ctx.DrawLine(Color.White, 2f, new PointF(xOffset + 720, yOffset + 10),
                        new PointF(xOffset + 720, yOffset + 55));
                    string scoreText = $"Score: {int.Parse(floorData.Node1.Score) + int.Parse(floorData.Node2.Score)}";
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(xOffset + 710, yOffset + 20),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Top
                    }, scoreText,
                        Color.White);

                    // Draw cycle number
                    if (gameMode == HsrEndGameMode.PureFiction)
                    {
                        FontRectangle size = TextMeasurer.MeasureSize(scoreText, new TextOptions(m_NormalFont));
                        ctx.DrawLine(Color.White, 2f, new PointF(xOffset + 695 - (int)size.Width, yOffset + 10),
                            new PointF(xOffset + 695 - (int)size.Width, yOffset + 55));

                        ctx.DrawText(new RichTextOptions(m_NormalFont)
                        {
                            Origin = new Vector2(xOffset + 650 - (int)size.Width, yOffset + 20),
                            HorizontalAlignment = HorizontalAlignment.Right,
                            VerticalAlignment = VerticalAlignment.Top
                        }, floorData.RoundNum.ToString(), Color.White);
                        ctx.DrawImage(m_CycleIcon, new Point(xOffset + 650 - (int)size.Width, yOffset + 10), 1f);
                    }

                    if (floorNumber % 2 == 1) yOffset += 620;
                }
            });

            MemoryStream stream = new();
            await background.SaveAsJpegAsync(stream, JpegEncoder);
            stream.Position = 0;

            m_Logger.LogInformation(LogMessage.CardGenSuccess, gameMode.GetString(),
                context.UserId, stopwatch.ElapsedMilliseconds);
            return stream;
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessage.CardGenError, gameMode.GetString(), context.UserId,
                JsonSerializer.Serialize(context.Data));
            throw new CommandException($"Failed to generate {gameMode.GetString()} card", e);
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
}
