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
using Mehrak.Domain.Utility;
using Mehrak.GameApi.Zzz.Types;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Services.Zzz.Defense;

internal class ZzzDefenseCardService : ICardService<ZzzDefenseData>, IAsyncInitializable
{
    private readonly IImageRepository m_ImageRepository;
    private readonly ILogger<ZzzDefenseCardService> m_Logger;

    private readonly Font m_TitleFont;
    private readonly Font m_NormalFont;

    private Dictionary<char, Image> m_RatingImages = [];
    private Dictionary<char, Image> m_SmallRatingImages = [];
    private Image m_BaseBuddyImage = null!;
    private Image m_BackgroundImage = null!;

    private static readonly string[] FrontierNames =
        ["First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh"];

    private static readonly JpegEncoder JpegEncoder = new()
    {
        Quality = 90,
        Interleaved = false
    };

    private static readonly Color OverlayColor = Color.FromRgba(0, 0, 0, 128);

    public ZzzDefenseCardService(IImageRepository imageRepository, ILogger<ZzzDefenseCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_Logger = logger;

        FontCollection collection = new();
        var fontFamily = collection.Add("Assets/Fonts/zzz.ttf");

        m_TitleFont = fontFamily.CreateFont(40, FontStyle.Bold);
        m_NormalFont = fontFamily.CreateFont(28, FontStyle.Regular);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        char[] rating = ['S', 'A', 'B'];
        m_RatingImages = await rating.ToAsyncEnumerable()
            .Select(async (x, token) =>
            {
                var image = await Image.LoadAsync(
                    await m_ImageRepository.DownloadFileToStreamAsync($"zzz_rating_{x}"), token);
                image.Mutate(ctx => ctx.Resize(80, 0));
                return (Rating: x, Image: image);
            })
            .ToDictionaryAsync(x => x.Rating, x => x.Image, cancellationToken: cancellationToken);
        m_SmallRatingImages = m_RatingImages.Select(x => (x.Key, x.Value.Clone(y => y.Resize(0, 50))))
            .ToDictionary();
        m_BaseBuddyImage = await Image.LoadAsync(await
                m_ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.Zzz.BuddyName, "base")),
            cancellationToken);
        m_BackgroundImage = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("zzz_shiyu_bg"),
            cancellationToken);

        m_Logger.LogInformation(LogMessage.ServiceInitialized, nameof(ZzzDefenseCardService));
    }

    public async Task<Stream> GetCardAsync(ICardGenerationContext<ZzzDefenseData> context)
    {
        m_Logger.LogInformation(LogMessage.CardGenStartInfo, "Defense", context.UserId);
        var stopwatch = Stopwatch.StartNew();

        var data = context.Data;
        List<IDisposable> disposables = [];
        try
        {
            var avatarImages = await data.AllFloorDetail
                .SelectMany(x => x.Node1.Avatars.Concat(x.Node2.Avatars))
                .DistinctBy(x => x.Id)
                .ToAsyncEnumerable()
                .Select(async (x, token) => new ZzzAvatar(x.Id, x.Level, x.Rarity[0], x.Rank, await Image.LoadAsync(
                    await m_ImageRepository.DownloadFileToStreamAsync(x.ToImageName()), token)))
                .ToDictionaryAsync(x => x,
                    x => x.GetStyledAvatarImage(), ZzzAvatarIdComparer.Instance);
            var buddyImages = await data.AllFloorDetail
                .SelectMany(x => new ZzzBuddy?[] { x.Node1.Buddy, x.Node2.Buddy })
                .Where(x => x is not null)
                .DistinctBy(x => x!.Id)
                .ToAsyncEnumerable()
                .ToDictionaryAsync(async (x, token) => await Task.FromResult(x!.Id),
                    async (x, token) =>
                        await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(x!.ToImageName()), token));

            disposables.AddRange(avatarImages.Keys);
            disposables.AddRange(avatarImages.Values);
            disposables.AddRange(buddyImages.Values);

            List<(int FloorNumber, FloorDetail? Data)> floorDetails =
            [
                .. Enumerable.Range(0, 7)
                    .Select(floorIndex =>
                    {
                        var floorData = data.AllFloorDetail
                            .FirstOrDefault(x => x.LayerIndex - 1 == floorIndex);
                        return (FloorNumber: floorIndex, Data: floorData);
                    })
            ];

            var lookup = avatarImages.GetAlternateLookup<int>();

            var height = 515 + floorDetails.Where(x => x.FloorNumber != 6).Chunk(2)
                .Select(x => x.All(y => y.Data == null || IsSmallBlob(y.Data)) ? 200 : 620).Sum();

            // 1550 x height
            var background = m_BackgroundImage.Clone(ctx =>
                ctx.Resize(new ResizeOptions
                {
                    CenterCoordinates = new PointF(ctx.GetCurrentSize().Width / 2f, ctx.GetCurrentSize().Height / 2f),
                    Size = new Size(1550, height),
                    Mode = ResizeMode.Crop,
                    Sampler = KnownResamplers.Bicubic
                }));
            disposables.Add(background);

            var tzi = context.GetParameter<Server>("server").GetTimeZoneInfo();

            background.Mutate(ctx =>
            {
                ctx.DrawText(new RichTextOptions(m_TitleFont)
                {
                    Origin = new Vector2(50, 80),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, "Shiyu Defense", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(50, 120),
                    VerticalAlignment = VerticalAlignment.Bottom
                },
                    $"{DateTimeOffset.FromUnixTimeSeconds(long.Parse(data.BeginTime))
                        .ToOffset(tzi.BaseUtcOffset):dd/MM/yyyy} - " +
                    $"{DateTimeOffset.FromUnixTimeSeconds(long.Parse(data.EndTime))
                        .ToOffset(tzi.BaseUtcOffset):dd/MM/yyyy}",
                    Color.White);

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(1500, 80),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Right
                }, $"{context.GameProfile.Nickname}·IK {context.GameProfile.Level}", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(1500, 110),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Right
                },
                    $"{context.GameProfile.GameUid}", Color.White);

                var ratingModule = ImageUtility.CreateRoundedRectanglePath(500, 90, 15).Translate(450, 30);
                ctx.Fill(OverlayColor, ratingModule);

                var ratingX = 470;
                foreach (var entry in m_RatingImages)
                {
                    ctx.DrawImage(entry.Value, new Point(ratingX, 35), 1f);
                    ctx.DrawText("x", m_NormalFont, Color.Gray, new PointF(ratingX + 85, 65));
                    ctx.DrawText(
                        data.RatingList.FirstOrDefault(x => x.Rating[0].Equals(entry.Key))?.Times.ToString() ?? "0",
                        m_TitleFont, Color.White, new PointF(ratingX + 110, 65));
                    ratingX += 160;
                }

                var yOffset = 150;
                IPath overlay;
                foreach ((var floorNumber, var floorData) in floorDetails)
                {
                    var xOffset = floorNumber % 2 * 750 + 50;

                    if (floorNumber == 6)
                    {
                        overlay = ImageUtility.CreateRoundedRectanglePath(1450, 335, 15).Translate(xOffset, yOffset);
                        ctx.Fill(OverlayColor, overlay);
                        ctx.DrawText(new RichTextOptions(m_NormalFont)
                        {
                            Origin = new Vector2(xOffset + 20, yOffset + 30),
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Top
                        }, $"{FrontierNames[floorNumber]} Frontier", Color.White);
                        if (floorData == null)
                        {
                            ctx.DrawText(new RichTextOptions(m_NormalFont)
                            {
                                Origin = new Vector2(xOffset + 750, yOffset + 180),
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center
                            }, "No Clear Records", Color.White);
                        }
                        else
                        {
                            ctx.DrawImage(m_SmallRatingImages[floorData.Rating[0]],
                                new Point(xOffset + 1380, yOffset + 10), 1f);
                            using var firstHalf = GetRosterImage(
                                [.. floorData.Node1!.Avatars.Select(x => lookup[x.Id])],
                                buddyImages.GetValueOrDefault(floorData.Node1.Buddy?.Id ?? -1));
                            using var secondHalf = GetRosterImage(
                                [.. floorData.Node2!.Avatars.Select(x => lookup[x.Id])],
                                buddyImages.GetValueOrDefault(floorData.Node2.Buddy?.Id ?? -1));

                            ctx.DrawLine(Color.White, 2f, new PointF(xOffset + 20, yOffset + 70),
                                new PointF(xOffset + 1430, yOffset + 70));

                            ctx.DrawText("Node 1", m_NormalFont, Color.White, new PointF(xOffset + 45, yOffset + 95));
                            ctx.DrawText(new RichTextOptions(m_NormalFont)
                            {
                                Origin = new Vector2(xOffset + 680, yOffset + 95),
                                HorizontalAlignment = HorizontalAlignment.Right
                            }, TimeSpan.FromSeconds(floorData.Node1.BattleTime).ToString("mm\\m\\ ss\\s"), Color.White);
                            ctx.DrawImage(firstHalf, new Point(xOffset + 40, yOffset + 130), 1f);

                            ctx.DrawLine(Color.White, 2f, new PointF(xOffset + 725, yOffset + 80),
                                new PointF(xOffset + 725, yOffset + 320));

                            ctx.DrawText("Node 2", m_NormalFont, Color.White, new PointF(xOffset + 770, yOffset + 95));
                            ctx.DrawText(new RichTextOptions(m_NormalFont)
                            {
                                Origin = new Vector2(xOffset + 1405, yOffset + 95),
                                HorizontalAlignment = HorizontalAlignment.Right
                            }, TimeSpan.FromSeconds(floorData.Node2.BattleTime).ToString("mm\\m\\ ss\\s"), Color.White);
                            ctx.DrawImage(secondHalf, new Point(xOffset + 765, yOffset + 130), 1f);
                        }

                        break;
                    }

                    if (floorData == null)
                    {
                        var isFast =
                            floorDetails.FirstOrDefault(x => x.FloorNumber > floorNumber && x.Data is not null)
                                .Data is not null;
                        var isBigBlob = false;
                        if ((floorNumber % 2 == 0 && floorNumber + 1 < floorDetails.Count &&
                             !IsSmallBlob(floorDetails[floorNumber + 1].Data)) ||
                            (floorNumber % 2 == 1 && floorNumber - 1 >= 0 &&
                             !IsSmallBlob(floorDetails[floorNumber - 1].Data)))
                        {
                            overlay = ImageUtility.CreateRoundedRectanglePath(700, 600, 15)
                                .Translate(xOffset, yOffset);
                            ctx.Fill(OverlayColor, overlay);
                            ctx.DrawText(new RichTextOptions(m_NormalFont)
                            {
                                Origin = new Vector2(xOffset + 350, yOffset + 300),
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center
                            }, isFast ? "Quick Clear" : "No Clear Records", Color.White);
                            isBigBlob = true;
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
                            }, isFast ? "Quick Clear" : "No Clear Records", Color.White);
                        }

                        ctx.DrawText(new RichTextOptions(m_NormalFont)
                        {
                            Origin = new Vector2(xOffset + 20, yOffset + 30),
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Top
                        },
                            $"{FrontierNames[floorNumber]} Frontier", Color.White);

                        if (floorNumber % 2 == 1) yOffset += isBigBlob ? 620 : 200;
                        continue;
                    }

                    overlay = ImageUtility.CreateRoundedRectanglePath(700, 600, 15).Translate(xOffset, yOffset);
                    ctx.Fill(OverlayColor, overlay);
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(xOffset + 20, yOffset + 30),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top
                    }, $"{FrontierNames[floorNumber]} Frontier", Color.White);
                    ctx.DrawImage(m_SmallRatingImages[floorData.Rating[0]], new Point(xOffset + 630, yOffset + 10), 1f);

                    using var node1 = GetRosterImage([.. floorData.Node1!.Avatars.Select(x => lookup[x.Id])],
                        buddyImages.GetValueOrDefault(floorData.Node1.Buddy?.Id ?? -1));
                    using var node2 = GetRosterImage([.. floorData.Node2!.Avatars.Select(x => lookup[x.Id])],
                        buddyImages.GetValueOrDefault(floorData.Node2.Buddy?.Id ?? -1));

                    ctx.DrawLine(Color.White, 2f, new PointF(xOffset + 20, yOffset + 70),
                        new PointF(xOffset + 680, yOffset + 70));
                    ctx.DrawText("Node 1", m_NormalFont, Color.White, new PointF(xOffset + 45, yOffset + 95));
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(xOffset + 655, yOffset + 95),
                        HorizontalAlignment = HorizontalAlignment.Right
                    }, TimeSpan.FromSeconds(floorData.Node1.BattleTime).ToString("mm\\m\\ ss\\s"), Color.White);

                    ctx.DrawImage(node1, new Point(xOffset + 25, yOffset + 130), 1f);
                    ctx.DrawLine(Color.White, 2f, new PointF(xOffset + 40, yOffset + 335),
                        new PointF(xOffset + 660, yOffset + 335));
                    ctx.DrawText("Node 2", m_NormalFont, Color.White, new PointF(xOffset + 45, yOffset + 360));
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(xOffset + 655, yOffset + 360),
                        HorizontalAlignment = HorizontalAlignment.Right
                    }, TimeSpan.FromSeconds(floorData.Node2.BattleTime).ToString("mm\\m\\ ss\\s"), Color.White);

                    ctx.DrawImage(node2, new Point(xOffset + 25, yOffset + 395), 1f);

                    if (floorNumber % 2 == 1) yOffset += 620;
                }
            });

            MemoryStream stream = new();
            await background.SaveAsJpegAsync(stream, JpegEncoder);
            stream.Position = 0;

            m_Logger.LogInformation(LogMessage.CardGenSuccess, "Defense", context.UserId,
                stopwatch.ElapsedMilliseconds);
            return stream;
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessage.CardGenError, "Defense", context.UserId, JsonSerializer.Serialize(data));
            throw new CommandException("Failed to generate Defense card", e);
        }
        finally
        {
            disposables.ForEach(x => x.Dispose());
        }
    }

    private Image<Rgba32> GetRosterImage(List<Image<Rgba32>> avatarImages, Image? buddyImage = null)
    {
        const int avatarWidth = 150;

        var offset = (3 - avatarImages.Count) * avatarWidth / 2 + 10;

        Image<Rgba32> rosterImage = new(650, 200);

        rosterImage.Mutate(ctx =>
        {
            ctx.Clear(Color.Transparent);
            var x = 0;

            for (var i = 0; i < avatarImages.Count; i++)
            {
                x = offset + i * (avatarWidth + 10);
                ctx.DrawImage(avatarImages[i], new Point(x, 0), 1f);
            }

            using Image<Rgba32> buddyBorder = new(150, 180);
            buddyBorder.Mutate(x =>
            {
                var outerPath = ImageUtility.CreateRoundedRectanglePath(150, 180, 15);
                x.Clear(Color.FromRgb(24, 24, 24));
                x.Draw(Color.Black, 4f, outerPath);
                x.DrawImage(buddyImage != null ? buddyImage : m_BaseBuddyImage, new Point(-45, 0), 1f);
                x.ApplyRoundedCorners(15);
            });
            x = offset + avatarImages.Count * (avatarWidth + 10);
            ctx.DrawImage(buddyBorder, new Point(x, 0), 1f);
        });

        return rosterImage;
    }

    private static bool IsSmallBlob(FloorDetail? detail)
    {
        return detail is null || detail.LayerIndex == 7;
    }
}
