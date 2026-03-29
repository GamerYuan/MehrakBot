#region

using System.Numerics;
using System.Text.Json;
using Mehrak.Application.Models;
using Mehrak.Application.Services.Abstractions;
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

    private readonly List<(int Boundary, Image Icon)> m_RankIcons = [];
    private Dictionary<string, Image> m_RatingImages = [];
    private Dictionary<string, Image> m_SmallRatingImages = [];
    private Image m_BaseBuddyImage = null!;
    private Image m_BackgroundImage = null!;

    private static readonly DrawingOptions m_RankIconTextDrawingOptions = new()
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
        m_SmallRatingImages = m_RatingImages.Select(x => (x.Key, x.Value.Clone(y => y.Resize(0, 50))))
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

            disposables.AddRange(avatarImages.Keys);
            disposables.AddRange(avatarImages.Values);
            disposables.AddRange(buddyImages.Values);

            var lookup = avatarImages.GetAlternateLookup<int>();

            var background = m_BackgroundImage.Clone(ctx =>
                ctx.Resize(new ResizeOptions
                {
                    CenterCoordinates = new PointF(ctx.GetCurrentSize().Width / 2f, ctx.GetCurrentSize().Height / 2f),
                    Size = new Size(1050, 1050),
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
                    Origin = new Vector2(1000, 80),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Right
                }, $"{context.GameProfile.Nickname}·IK {context.GameProfile.Level}", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(1000, 110),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Right
                },
                    $"{context.GameProfile.GameUid}", Color.White);

                var briefModule = ImageUtility.CreateRoundedRectanglePath(950, 80, 15).Translate(50, 120);
                ctx.Fill(OverlayColor, briefModule);

                var totalScoreText = $"Total Score: {data.Brief.Score}";
                var totalScoreTextOptions = new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(70, 150),
                    VerticalAlignment = VerticalAlignment.Top
                };
                var totalScoreBounds =
                    TextMeasurer.MeasureBounds(totalScoreText, totalScoreTextOptions);

                ctx.DrawText(totalScoreTextOptions, totalScoreText, Color.White);
                //ctx.DrawImage(m_RankIcons.First(x => data.Brief.RankPercent <= x.Boundary).Icon,
                //    new Point(15 + (int)totalScoreBounds.Right, 135), 1f);
                //ctx.DrawText(new RichTextOptions(m_SmallFont)
                //{
                //    Origin = new Vector2(25 + (int)totalScoreBounds.Right, 152),
                //    VerticalAlignment = VerticalAlignment.Top,
                //}, $"{(float)data.Brief.RankPercent / 100:N2}%", Color.Black);
                using var rankIcon = GetRankIcon(data.Brief);
                ctx.DrawImage(rankIcon, new Point(15 + (int)totalScoreBounds.Right, 135), 1f);
                ctx.DrawImage(m_RatingImages[data.Brief.Rating], new Point(900, 140), 1f);
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
            ctx.DrawText(m_RankIconTextDrawingOptions, rankText, m_SmallFont, Color.White, new PointF(10, 17));
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

    private static bool IsSmallBlob(FloorDetail? detail)
    {
        return detail is null || detail.LayerIndex == 7;
    }
}
