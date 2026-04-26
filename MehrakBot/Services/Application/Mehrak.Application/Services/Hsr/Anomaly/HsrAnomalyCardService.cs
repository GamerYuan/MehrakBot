#region

using System.Numerics;
using Mehrak.Application.Models;
using Mehrak.Application.Renderers;
using Mehrak.Application.Renderers.Extensions;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Utility;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.GameApi.Hsr.Types;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Services.Hsr.Anomaly;

internal class HsrAnomalyCardService : CardServiceBase<HsrAnomalyInformation>
{
    private Image m_StarLit = null!;
    private Image m_StarUnlit = null!;
    private Image m_BossStarLit = null!;
    private Image m_BossStarUnlit = null!;
    private Image m_CycleIcon = null!;

    public HsrAnomalyCardService(IImageRepository imageRepository,
        ILogger<HsrAnomalyCardService> logger,
        IApplicationMetrics metrics)
        : base(
            "Hsr AA",
            imageRepository,
            logger,
            metrics,
            LoadFonts("Assets/Fonts/hsr.ttf", titleSize: 40, normalSize: 28, smallSize: 20))
    {
    }

    public override async Task LoadStaticResourcesAsync(CancellationToken cancellationToken = default)
    {
        m_StarLit = await Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync("hsr_moc_star", cancellationToken),
            cancellationToken);
        m_StarUnlit = m_StarLit.Clone(ctx =>
        {
            ctx.Grayscale();
            ctx.Brightness(0.7f);
        });

        m_BossStarLit = await Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync("hsr_anomaly_star", cancellationToken),
            cancellationToken);
        m_BossStarUnlit = m_BossStarLit.Clone(ctx =>
        {
            ctx.Grayscale();
            ctx.Brightness(0.7f);
        });

        m_CycleIcon = await Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync("hsr_hourglass", cancellationToken),
            cancellationToken);

        StaticBackground = await Image.LoadAsync<Rgba32>(
            await ImageRepository.DownloadFileToStreamAsync("hsr_aa_bg", cancellationToken),
            cancellationToken);
    }

    protected override Image<Rgba32> CreateBackground()
    {
        return StaticBackground!.CloneAs<Rgba32>();
    }

    public override async Task RenderCardAsync(
        Image<Rgba32> background,
        ICardGenerationContext<HsrAnomalyInformation> context,
        DisposableBag disposables,
        CancellationToken cancellationToken = default)
    {
        var anomalyData = context.Data;

        var bestRecord = anomalyData.BestRecord.RankIconType != RankIconType.ChallengePeakRankIconTypeNone
            ? anomalyData.ChallengeRecords.First(
                x => x.HasChallengeRecord && x.BossStars == anomalyData.BestRecord.BossStars
                    && x.MobStars == anomalyData.BestRecord.MobStars)
            : anomalyData.ChallengeRecords.First(x => x.HasChallengeRecord && x.MobStars == anomalyData.BestRecord.MobStars);

        var avatarImages = await bestRecord.MobRecords.SelectMany(x => x.Avatars)
            .Concat(bestRecord.BossRecord?.Avatars ?? [])
            .DistinctBy(x => x.Id)
            .ToAsyncEnumerable()
            .Select(async (x, token) =>
            {
                await using var stream = await ImageRepository.DownloadFileToStreamAsync(x.ToImageName(), token);
                var image = await Image.LoadAsync(stream, token);
                var avatar = new HsrAvatar(x.Id, x.Level, x.Rarity, x.Rank, image);
                disposables.Add(avatar);
                return avatar;
            })
            .ToDictionaryAsync(x => x,
                x =>
                {
                    var styledImage = x.GetStyledAvatarImage();
                    disposables.Add(styledImage);
                    return styledImage;
                },
                HsrAvatarIdComparer.Instance);

        var bossImage = await LoadImageFromRepositoryAsync(
            bestRecord.BossInfo.ToImageName(), disposables, cancellationToken);
        var buffImage = bestRecord.BossRecord != null
            ? await LoadImageFromRepositoryAsync(
                bestRecord.BossRecord.Buff.ToImageName(), disposables, cancellationToken)
            : null;
        var medalImage = await LoadImageFromRepositoryAsync<Rgba32>(
            anomalyData.ToMedalName(), disposables, cancellationToken);

        var lookup = avatarImages.GetAlternateLookup<int>();

        background.Mutate(ctx =>
        {
            ctx.DrawText(new RichTextOptions(Fonts.Title)
            {
                Origin = new Vector2(50, 80),
                VerticalAlignment = VerticalAlignment.Bottom
            }, "Anomaly Arbitration", Color.White);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(50, 110),
                VerticalAlignment = VerticalAlignment.Bottom
            },
                $"Version {bestRecord.Group.GameVersion}",
                Color.White);

            ctx.DrawImage(medalImage, new Point(465, 20), 1f);
            RichTextOptions bossStarTextOptions = new(Fonts.Title)
            {
                Origin = new Vector2(600, 100),
                VerticalAlignment = VerticalAlignment.Bottom
            };
            var bossStarBounds = TextMeasurer.MeasureBounds(bestRecord.BossStars.ToString(), bossStarTextOptions);
            ctx.DrawText(bossStarTextOptions, bestRecord.BossStars.ToString(), Color.White);
            ctx.DrawImage(m_BossStarLit, new Point((int)bossStarBounds.Right + 5, 50), 1f);

            RichTextOptions mobStarTextOptions = new(Fonts.Title)
            {
                Origin = new Vector2(bossStarBounds.Right + m_BossStarLit.Width + 25, 100),
                VerticalAlignment = VerticalAlignment.Bottom
            };
            ctx.DrawText(mobStarTextOptions, bestRecord.MobStars.ToString(), Color.White);
            ctx.DrawImage(m_StarLit,
                new Point((int)TextMeasurer.MeasureBounds(bestRecord.MobStars.ToString(), mobStarTextOptions).Right + 5, 50), 1f);

            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(1300, 80),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom
            },
                $"{context.GameProfile.Nickname} • TB {context.GameProfile.Level}", Color.White);

            var yOffset = 150;
            var bossClear = GetBossImage(bestRecord.BossRecord, bestRecord.BossInfo, bossImage, buffImage, lookup);
            disposables.Add(bossClear);
            ctx.DrawImage(bossClear, new Point(100, yOffset), 1f);

            foreach (var mobRecord in bestRecord.MobRecords.OrderBy(x => x.MazeId))
            {
                yOffset += 350;
                var mobInfo = bestRecord.MobInfo.First(x => x.MazeId == mobRecord.MazeId);
                var mobClear = GetMobImage(mobRecord, mobInfo, lookup);
                disposables.Add(mobClear);
                ctx.DrawImage(mobClear, new Point(225, yOffset), 1f);
            }
        });
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
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(300, 43),
                VerticalAlignment = VerticalAlignment.Center,
            }, $"{bossInfo.Name}", Color.White);
            for (var i = 0; i < 3; i++)
                ctx.DrawImage(i < record?.StarNum ? m_BossStarLit : m_BossStarUnlit,
                    new Point(975 + i * 50, 15), 1f);

            if (record != null)
            {
                using var rosterImage = RosterImageBuilder.Build(
                    record.Avatars.Select(x => avatarLookup[x.Id]),
                    new RosterLayout(MaxSlots: 4));
                ctx.DrawImage(rosterImage, new Point(330, 90), 1f);

                ctx.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(900, 30),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top
                }, record.RoundNum.ToString(), Color.White);
                ctx.DrawImage(m_CycleIcon, new Point(900, 20), 1f);

                ctx.DrawCenteredIcon(buffImage!, new PointF(1055, 170), 55, 0, Color.Black, Color.White);
            }
            else
            {
                ctx.DrawText(new RichTextOptions(Fonts.Normal)
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
            ctx.DrawText(new RichTextOptions(text.Length > 50 ? Fonts.Small! : Fonts.Normal)
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
                ctx.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(450, 170),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                }, record.IsFast ? "Quick Clear" : "No Clear Records", Color.White);
            }
            else
            {
                using var rosterImage = RosterImageBuilder.Build(
                    record.Avatars.Select(x => avatarLookup[x.Id]),
                    new RosterLayout(MaxSlots: 4));
                ctx.DrawImage(rosterImage, new Point(125, 90), 1f);
                ctx.DrawText(new RichTextOptions(Fonts.Normal)
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
}
