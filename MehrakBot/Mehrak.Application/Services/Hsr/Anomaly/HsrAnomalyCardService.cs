using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Mehrak.Application.Models;
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

namespace Mehrak.Application.Services.Hsr.Anomaly;

internal class HsrAnomalyCardService : ICardService<HsrAnomalyInformation>, IAsyncInitializable
{
    private readonly IImageRepository m_ImageRepository;
    private readonly ILogger<HsrAnomalyCardService> m_Logger;

    private static readonly JpegEncoder JpegEncoder = new()
    {
        Interleaved = false,
        Quality = 90,
        ColorType = JpegEncodingColor.Rgb
    };

    private static readonly Color OverlayColor = Color.FromRgba(0, 0, 0, 128);

    private readonly Font m_TitleFont;
    private readonly Font m_NormalFont;
    private readonly Font m_SmallFont;

    private Image m_StarLit = null!;
    private Image m_StarUnlit = null!;
    private Image m_BossStarLit = null!;
    private Image m_BossStarUnlit = null!;
    private Image m_CycleIcon = null!;
    private Image m_Background = null!;

    public HsrAnomalyCardService(IImageRepository imageRepository, ILogger<HsrAnomalyCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_Logger = logger;

        FontCollection collection = new();
        var fontFamily = collection.Add("Assets/Fonts/hsr.ttf");

        m_TitleFont = fontFamily.CreateFont(40, FontStyle.Bold);
        m_NormalFont = fontFamily.CreateFont(28, FontStyle.Regular);
        m_SmallFont = fontFamily.CreateFont(20, FontStyle.Regular);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        m_StarLit = await Image.LoadAsync(
            await m_ImageRepository.DownloadFileToStreamAsync("hsr_moc_star", cancellationToken),
            cancellationToken);
        m_StarUnlit = m_StarLit.Clone(ctx =>
        {
            ctx.Grayscale();
            ctx.Brightness(0.7f);
        });

        m_BossStarLit = await Image.LoadAsync(
            await m_ImageRepository.DownloadFileToStreamAsync("hsr_anomaly_star", cancellationToken),
            cancellationToken);
        m_BossStarUnlit = m_BossStarLit.Clone(ctx =>
        {
            ctx.Grayscale();
            ctx.Brightness(0.7f);
        });

        m_CycleIcon = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("hsr_hourglass"),
            cancellationToken);

        m_Background = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("hsr_aa_bg"),
            cancellationToken);

    }

    public async Task<Stream> GetCardAsync(ICardGenerationContext<HsrAnomalyInformation> context)
    {
        m_Logger.LogInformation(LogMessage.CardGenStartInfo, "Anomaly Arbitration", context.UserId);
        var stopwatch = Stopwatch.StartNew();

        var anomalyData = context.Data;
        List<IDisposable> disposableResources = [];

        try
        {
            var bestRecord = anomalyData.BestRecord.RankIconType != RankIconType.ChallengePeakRankIconTypeNone
                ? anomalyData.ChallengeRecords.First(
                    x => x.HasChallengeRecord && x.BossStars == anomalyData.BestRecord.BossStars
                        && x.MobStars == anomalyData.BestRecord.MobStars)
                : anomalyData.ChallengeRecords.First(x => x.HasChallengeRecord && x.MobStars == anomalyData.BestRecord.MobStars);

            var avatarImages = await bestRecord.MobRecords.SelectMany(x => x.Avatars)
                .Concat(bestRecord.BossRecord?.Avatars ?? [])
                .DistinctBy(x => x.Id)
                .ToAsyncEnumerable()
                .Select(async (x, token) => new HsrAvatar(x.Id, x.Level, x.Rarity, x.Rank,
                    await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(x.ToImageName(), token), token)))
                .ToDictionaryAsync(x => x,
                    x => x.GetStyledAvatarImage(),
                    HsrAvatarIdComparer.Instance);
            var bossImage = await Image.LoadAsync(
                await m_ImageRepository.DownloadFileToStreamAsync(bestRecord.BossInfo.ToImageName()));
            var buffImage = bestRecord.BossRecord != null
                ? await Image.LoadAsync(
                    await m_ImageRepository.DownloadFileToStreamAsync(bestRecord.BossRecord.Buff.ToImageName()))
                : null;
            var medalImage = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(anomalyData.ToMedalName()));

            buffImage?.Mutate(ctx => ctx.Resize(110, 0));

            var lookup = avatarImages.GetAlternateLookup<int>();

            var background = m_Background.CloneAs<Rgba32>();

            background.Mutate(ctx =>
            {
                ctx.DrawText(new RichTextOptions(m_TitleFont)
                {
                    Origin = new Vector2(50, 80),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, "Anomaly Arbitration", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(50, 110),
                    VerticalAlignment = VerticalAlignment.Bottom
                },
                    $"{bestRecord.Group.BeginTime.Day}/{bestRecord.Group.BeginTime.Month}/{bestRecord.Group.BeginTime.Year} - " +
                    $"{bestRecord.Group.EndTime.Day}/{bestRecord.Group.EndTime.Month}/{bestRecord.Group.EndTime.Year}",
                    Color.White);

                ctx.DrawImage(medalImage, new Point(465, 20), 1f);
                RichTextOptions bossStarTextOptions = new(m_TitleFont)
                {
                    Origin = new Vector2(600, 100),
                    VerticalAlignment = VerticalAlignment.Bottom
                };
                var bossStarBounds = TextMeasurer.MeasureBounds(bestRecord.BossStars.ToString(), bossStarTextOptions);
                ctx.DrawText(bossStarTextOptions, bestRecord.BossStars.ToString(), Color.White);
                ctx.DrawImage(m_BossStarLit, new Point((int)bossStarBounds.Right + 5, 50), 1f);

                RichTextOptions mobStarTextOptions = new(m_TitleFont)
                {
                    Origin = new Vector2(bossStarBounds.Right + m_BossStarLit.Width + 25, 100),
                    VerticalAlignment = VerticalAlignment.Bottom
                };
                ctx.DrawText(mobStarTextOptions, bestRecord.MobStars.ToString(), Color.White);
                ctx.DrawImage(m_StarLit,
                    new Point((int)TextMeasurer.MeasureBounds(bestRecord.MobStars.ToString(), mobStarTextOptions).Right + 5, 50), 1f);

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(1300, 80),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom
                },
                    $"{context.GameProfile.Nickname} • TB {context.GameProfile.Level}", Color.White);

                var yOffset = 150;
                using var bossClear = GetBossImage(bestRecord.BossRecord, bestRecord.BossInfo, bossImage, buffImage, lookup);
                ctx.DrawImage(bossClear, new Point(100, yOffset), 1f);

                foreach (var mobRecord in bestRecord.MobRecords.OrderBy(x => x.MazeId))
                {
                    yOffset += 350;
                    var mobInfo = bestRecord.MobInfo.First(x => x.MazeId == mobRecord.MazeId);
                    using var mobClear = GetMobImage(mobRecord, mobInfo, lookup);
                    ctx.DrawImage(mobClear, new Point(225, yOffset), 1f);
                }
            });

            MemoryStream stream = new();
            await background.SaveAsJpegAsync(stream, JpegEncoder);
            stream.Position = 0;

            m_Logger.LogInformation(LogMessage.CardGenSuccess, "Anomaly Arbitration", context.UserId,
                stopwatch.ElapsedMilliseconds);
            return stream;
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessage.CardGenError, "Anomaly Arbitration", context.UserId,
                JsonSerializer.Serialize(anomalyData));
            throw new CommandException("Failed to generate Anomaly Arbitration card", e);
        }
        finally
        {
            disposableResources.ForEach(x => x.Dispose());
        }
    }

    private Image<Rgba32> GetBossImage(BossRecord? record, BossInfo bossInfo,
        Image bossImage, Image? buffImage,
        Dictionary<HsrAvatar, Image<Rgba32>>.AlternateLookup<int> avatarLookup)
    {
        Image<Rgba32> image = new(1150, 300);
        image.Mutate(ctx =>
        {
            ctx.Clear(OverlayColor);

            ctx.DrawImage(bossImage, new Point(0, 0), 1f);
            ctx.DrawText(new RichTextOptions(m_NormalFont)
            {
                Origin = new Vector2(300, 43),
                VerticalAlignment = VerticalAlignment.Center,
            }, $"{bossInfo.Name}", Color.White);
            for (var i = 0; i < 3; i++)
                ctx.DrawImage(i < record?.StarNum ? m_BossStarLit : m_BossStarUnlit,
                    new Point(975 + i * 50, 15), 1f);

            if (record != null)
            {
                using var rosterImage =
                    GetRosterImage([.. record.Avatars.Select(x => x.Id)], avatarLookup);
                ctx.DrawImage(rosterImage, new Point(330, 90), 1f);

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(900, 30),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top
                }, record.RoundNum.ToString(), Color.White);
                ctx.DrawImage(m_CycleIcon, new Point(900, 20), 1f);

                IPath ellipse = new EllipsePolygon(new PointF(1055, 170), 55);
                ctx.Fill(Color.Black, ellipse);
                ctx.Draw(Color.White, 1f, ellipse);
                ctx.DrawImage(buffImage!, new Point(1000, 120), 1f);
            }
            else
            {
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(550, 170),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                }, "No Clear Records", Color.White);
            }

            ctx.ApplyRoundedCorners(15);
        });
        return image;
    }

    private Image<Rgba32> GetMobImage(MobRecord record, MobInfo floorInfo,
        Dictionary<HsrAvatar, Image<Rgba32>>.AlternateLookup<int> avatarLookup)
    {
        Image<Rgba32> image = new(900, 300);
        image.Mutate(ctx =>
        {
            ctx.Clear(OverlayColor);

            var text = $"{floorInfo.Name} : {floorInfo.MonsterName}";
            ctx.DrawText(new RichTextOptions(text.Length > 50 ? m_SmallFont : m_NormalFont)
            {
                Origin = new Vector2(30, 43),
                VerticalAlignment = VerticalAlignment.Center,
                WrappingLength = 600
            }, text, Color.White);

            for (var i = 0; i < 3; i++)
                ctx.DrawImage(i < record.StarNum ? m_StarLit : m_StarUnlit,
                    new Point(725 + i * 50, 15), 1f);

            if (record.Avatars.Count == 0)
            {
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(450, 170),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                }, record.IsFast ? "Quick Clear" : "No Clear Records", Color.White);
            }
            else
            {
                using var rosterImage = GetRosterImage([.. record.Avatars.Select(x => x.Id)], avatarLookup);
                ctx.DrawImage(rosterImage, new Point(125, 90), 1f);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(650, 30),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top
                }, record.RoundNum.ToString(), Color.White);
                ctx.DrawImage(m_CycleIcon, new Point(650, 20), 1f);
            }

            ctx.ApplyRoundedCorners(15);
        });
        return image;
    }

    private static Image<Rgba32> GetRosterImage(List<int> avatarIds,
        Dictionary<HsrAvatar, Image<Rgba32>>.AlternateLookup<int> imageDict)
    {
        const int avatarWidth = 150;

        var offset = (4 - avatarIds.Count) * avatarWidth / 2 + 10;

        Image<Rgba32> rosterImage = new(650, 200);

        rosterImage.Mutate(ctx =>
        {
            ctx.Clear(Color.Transparent);

            for (var i = 0; i < avatarIds.Count; i++)
            {
                var x = offset + i * (avatarWidth + 10);
                ctx.DrawImage(imageDict[avatarIds[i]], new Point(x, 0), 1f);
            }
        });

        return rosterImage;
    }
}
