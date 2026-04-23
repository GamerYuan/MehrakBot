#region

using System.Numerics;
using System.Text.Json;
using Mehrak.Application.Models;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Renderers.Extensions;
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

internal class ZzzDefenseCardService : ICardService<ZzzDefenseDataV2>, IAsyncInitializable
{
    private readonly IImageRepository m_ImageRepository;
    private readonly ILogger<ZzzDefenseCardService> m_Logger;
    private readonly IApplicationMetrics m_Metrics;

    private readonly Font m_TitleFont;
    private readonly Font m_NormalFont;
    private readonly Font m_SmallFont;
    private readonly Font m_EvenSmallerFont;

    private readonly List<(int Boundary, Image Icon)> m_RankIcons = [];
    private Dictionary<string, Image> m_RatingImages = [];
    private Dictionary<string, Image> m_SmallRatingImages = [];
    private Image m_BaseBuddyImage = null!;
    private Image m_BackgroundImage = null!;

    private static readonly DrawingOptions RankIconTextDrawingOptions = new()
    {
        GraphicsOptions = new GraphicsOptions()
        {
            AlphaCompositionMode = PixelAlphaCompositionMode.Xor
        }
    };

    private static readonly JpegEncoder JpegEncoder = new()
    {
        Quality = 90,
        Interleaved = false
    };

    private static readonly Color OverlayColor = Color.FromRgba(0, 0, 0, 128);

    public ZzzDefenseCardService(IImageRepository imageRepository, ILogger<ZzzDefenseCardService> logger, IApplicationMetrics metrics)
    {
        m_ImageRepository = imageRepository;
        m_Logger = logger;
        m_Metrics = metrics;

        FontCollection collection = new();
        var fontFamily = collection.Add("Assets/Fonts/zzz.ttf");

        m_TitleFont = fontFamily.CreateFont(40, FontStyle.Bold);
        m_NormalFont = fontFamily.CreateFont(28, FontStyle.Regular);
        m_SmallFont = fontFamily.CreateFont(20, FontStyle.Regular);
        m_EvenSmallerFont = fontFamily.CreateFont(18, FontStyle.Regular);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        string[] rating = ["S+", "S", "A", "B"];
        m_RatingImages = await rating.ToAsyncEnumerable()
            .Select(async (x, token) =>
            {
                var image = await Image.LoadAsync(
                    await m_ImageRepository.DownloadFileToStreamAsync($"zzz_rating_{x}", token), token);
                image.Mutate(ctx => ctx.Resize(80, 0));
                return (Rating: x, Image: image);
            })
            .ToDictionaryAsync(x => x.Rating, x => x.Image, cancellationToken: cancellationToken);
        m_SmallRatingImages = m_RatingImages.Select(x => (x.Key, x.Value.Clone(y => y.Resize(0, 40))))
            .ToDictionary();
        m_BaseBuddyImage = await Image.LoadAsync(await
                m_ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.Zzz.BuddyName, "base")),
            cancellationToken);
        m_BackgroundImage = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("zzz_shiyu_bg"),
            cancellationToken);

        m_RankIcons.Add((199, await Image.LoadAsync(
            await m_ImageRepository.DownloadFileToStreamAsync("zzz_rank_bg_1", cancellationToken),
            cancellationToken)));
        m_RankIcons.Add((299, await Image.LoadAsync(
            await m_ImageRepository.DownloadFileToStreamAsync("zzz_rank_bg_2", cancellationToken),
            cancellationToken)));
        m_RankIcons.Add((599, await Image.LoadAsync(
            await m_ImageRepository.DownloadFileToStreamAsync("zzz_rank_bg_3", cancellationToken),
            cancellationToken)));
        m_RankIcons.Add((2099, await Image.LoadAsync(
            await m_ImageRepository.DownloadFileToStreamAsync("zzz_rank_bg_4", cancellationToken),
            cancellationToken)));
        m_RankIcons.Add((int.MaxValue, await Image.LoadAsync(
            await m_ImageRepository.DownloadFileToStreamAsync("zzz_rank_bg_5", cancellationToken),
            cancellationToken)));

        m_Logger.LogInformation(LogMessage.ServiceInitialized, nameof(ZzzDefenseCardService));
    }

    public async Task<Stream> GetCardAsync(ICardGenerationContext<ZzzDefenseDataV2> context)
    {
        using var cardGenTimer = m_Metrics.ObserveCardGenerationDuration("zzz defense");
        m_Logger.LogInformation(LogMessage.CardGenStartInfo, "Defense", context.UserId);

        var data = context.Data;

        if (data.FifthLayerDetail == null || data.Brief == null)
        {
            m_Logger.LogInformation(LogMessage.NoClearRecords, "Defense", context.UserId, context.GameProfile.GameUid);
            throw new CommandException("No clear records found for Defense");
        }

        List<IDisposable> disposables = [];
        try
        {
            var avatarImages = await data.FifthLayerDetail.LayerChallengeInfoList
                .SelectMany(x => x.AvatarList)
                .DistinctBy(x => x.Id)
                .ToAsyncEnumerable()
                .Select(async (x, token) => new ZzzAvatar(x.Id, x.Level, x.Rarity[0], x.Rank, await Image.LoadAsync(
                    await m_ImageRepository.DownloadFileToStreamAsync(x.ToImageName()), token)))
                .ToDictionaryAsync(x => x,
                    x => x.GetStyledAvatarImage(), ZzzAvatarIdComparer.Instance);
            var buddyImages = await data.FifthLayerDetail.LayerChallengeInfoList
                .Select(x => x.Buddy)
                .Where(x => x is not null)
                .DistinctBy(x => x!.Id)
                .ToAsyncEnumerable()
                .ToDictionaryAsync(async (x, token) => await Task.FromResult(x!.Id),
                    async (x, token) =>
                        await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(x!.ToImageName()), token));
            var bossImages = await data.FifthLayerDetail.LayerChallengeInfoList
                .ToAsyncEnumerable()
                .ToDictionaryAsync(async (x, token) => await Task.FromResult(x.LayerId),
                    async (x, token) => await Image.LoadAsync(
                        await m_ImageRepository.DownloadFileToStreamAsync(x.ToMonsterImageName(), token), token));

            disposables.AddRange(avatarImages.Keys);
            disposables.AddRange(avatarImages.Values);
            disposables.AddRange(buddyImages.Values);
            disposables.AddRange(bossImages.Values);

            var lookup = avatarImages.GetAlternateLookup<int>();

            var background = m_BackgroundImage.Clone(ctx =>
                ctx.Resize(new ResizeOptions
                {
                    CenterCoordinates = new PointF(ctx.GetCurrentSize().Width / 2f, ctx.GetCurrentSize().Height / 2f),
                    Size = new Size(1000, 1050),
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
                    Origin = new Vector2(50, 110),
                    VerticalAlignment = VerticalAlignment.Bottom
                },
                    $"{DateTimeOffset.FromUnixTimeSeconds(long.Parse(data.BeginTime))
                        .ToOffset(tzi.BaseUtcOffset):dd/MM/yyyy} - " +
                    $"{DateTimeOffset.FromUnixTimeSeconds(long.Parse(data.EndTime))
                        .ToOffset(tzi.BaseUtcOffset):dd/MM/yyyy}",
                    Color.White);

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(950, 80),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Right
                }, $"{context.GameProfile.Nickname}·IK {context.GameProfile.Level}", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(950, 110),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Right
                },
                    $"{context.GameProfile.GameUid}", Color.White);

                ctx.DrawRoundedRectangleOverlay(900, 80, new PointF(50, 120),
                    new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));

                var totalScoreText = $"Total Score: {data.Brief.Score}";
                var totalScoreTextOptions = new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(70, 150),
                    VerticalAlignment = VerticalAlignment.Top
                };
                var totalScoreBounds =
                    TextMeasurer.MeasureBounds(totalScoreText, totalScoreTextOptions);

                ctx.DrawText(totalScoreTextOptions, totalScoreText, Color.White);
                using var rankIcon = GetRankIcon(data.Brief);
                ctx.DrawImage(rankIcon, new Point(15 + (int)totalScoreBounds.Right, 135), 1f);
                ctx.DrawImage(m_RatingImages[data.Brief.Rating], new Point(850, 140), 1f);

                var i = 0;
                foreach (var floor in data.FifthLayerDetail.LayerChallengeInfoList)
                {
                    using var floorImage = GetFloorImage(floor, lookup, bossImages[floor.LayerId],
                        floor.Buddy is not null ? buddyImages[floor.Buddy!.Id] : null);
                    ctx.DrawImage(floorImage, new Point(50, 220 + i * 270), 1f);
                    i++;
                }
            });

            MemoryStream stream = new();
            await background.SaveAsJpegAsync(stream, JpegEncoder);
            stream.Position = 0;

            m_Logger.LogInformation(LogMessage.CardGenSuccess, "Defense", context.UserId);
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

    private Image<Rgba32> GetRankIcon(HadalBrief brief)
    {

        var image = m_RankIcons.First(x => brief.RankPercent <= x.Boundary).Icon.CloneAs<Rgba32>();
        image.Mutate(ctx =>
        {
            var rankText = $"{(float)brief.RankPercent / 100:N2}%";
            var size = TextMeasurer.MeasureSize(rankText, new TextOptions(m_SmallFont));
            ctx.DrawText(RankIconTextDrawingOptions, rankText,
                size.Width <= 80 ? m_SmallFont : m_EvenSmallerFont, Color.White, new PointF(8, 17));
        });
        return image;
    }

    private Image<Rgba32> GetFloorImage(HadalChallengeInfo floor,
        Dictionary<ZzzAvatar, Image<Rgba32>>.AlternateLookup<int> avatarLookup,
        Image bossImage,
        Image? buddyImage = null)
    {
        Image<Rgba32> image = new(900, 260);
        image.Mutate(ctx =>
        {
            ctx.Clear(OverlayColor);
            ctx.DrawText(new RichTextOptions(m_NormalFont)
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                Origin = new Vector2(800, 25)
            }, floor.Score.ToString(), Color.White);
            ctx.DrawText(new RichTextOptions(m_NormalFont)
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                Origin = new Vector2(680, 25)
            }, $"{floor.BattleTime}s", Color.White);
            ctx.DrawImage(m_SmallRatingImages[floor.Rating], new Point(800, 10), 1f);

            ctx.DrawImage(bossImage, new Point(0, 0), 1f);
            using var rosterImage =
                GetRosterImage([.. floor.AvatarList.Select(x => avatarLookup[x.Id])], buddyImage);
            ctx.DrawImage(rosterImage, new Point(220, 60), 1f);

            ctx.ApplyRoundedCorners(15);
        });
        return image;
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
}
