#region

using System.Numerics;
using Mehrak.Application.Renderers.Extensions;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Renderers;
using Mehrak.Application.Shared.Renderers.Extensions;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Models;
using Mehrak.Domain.User.Abstractions;
using Mehrak.GameApi.Hsr.Types;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Hsr.Anomaly;

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
        var downloadTasks = new[]
        {
            ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hsr.MoCStarName, cancellationToken),
            ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hsr.AnomalyStarName, cancellationToken),
            ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hsr.HourglassName, cancellationToken),
            ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hsr.AABackgroundName, cancellationToken)
        };
        await Task.WhenAll(downloadTasks);

        await using var starStream = downloadTasks[0].Result;
        await using var bossStarStream = downloadTasks[1].Result;
        await using var cycleStream = downloadTasks[2].Result;
        await using var bgStream = downloadTasks[3].Result;

        m_StarLit = await Image.LoadAsync(starStream, cancellationToken);
        m_StarUnlit = m_StarLit.Clone(ctx =>
        {
            ctx.Grayscale();
            ctx.Brightness(0.7f);
        });

        m_BossStarLit = await Image.LoadAsync(bossStarStream, cancellationToken);
        m_BossStarUnlit = m_BossStarLit.Clone(ctx =>
        {
            ctx.Grayscale();
            ctx.Brightness(0.7f);
        });

        m_CycleIcon = await Image.LoadAsync(cycleStream, cancellationToken);

        StaticBackground = await Image.LoadAsync<Rgba32>(bgStream, cancellationToken);
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
            ? anomalyData.ChallengeRecords.FirstOrDefault(
                x => x.HasChallengeRecord && x.BossStars == anomalyData.BestRecord.BossStars
                    && x.MobStars == anomalyData.BestRecord.MobStars)
            : anomalyData.ChallengeRecords.FirstOrDefault(x => x.HasChallengeRecord && x.MobStars == anomalyData.BestRecord.MobStars);

        if (bestRecord is null)
            throw new InvalidOperationException("No matching challenge record found for the best record configuration");

        var avatarData = bestRecord.MobRecords.SelectMany(x => x.Avatars)
            .Concat(bestRecord.BossRecord?.Avatars ?? [])
            .DistinctBy(x => x.Id)
            .ToList();

        var avatarTasks = avatarData
            .Select(async x =>
            {
                await using var stream = await ImageRepository.DownloadFileToStreamAsync(x.ToImageName(), cancellationToken);
                var image = await Image.LoadAsync(stream, cancellationToken);
                var avatar = new HsrAvatar(x.Id, x.Level, x.Rarity, x.Rank, image);
                disposables.Add(avatar);
                return avatar;
            })
            .ToList();

        var bossImageTask = LoadImageFromRepositoryAsync(
            bestRecord.BossInfo.ToImageName(), disposables, cancellationToken);
        var buffImageTask = bestRecord.BossRecord != null
            ? LoadImageFromRepositoryAsync(
                bestRecord.BossRecord.Buff.ToImageName(), disposables, cancellationToken)
            : Task.FromResult<Image>(null!);
        var medalImageTask = LoadImageFromRepositoryAsync<Rgba32>(
            anomalyData.ToMedalName(), disposables, cancellationToken);

        var allTasks = avatarTasks.Cast<Task>()
            .Append(bossImageTask)
            .Append(buffImageTask)
            .Append(medalImageTask);
        await Task.WhenAll(allTasks);

        var avatarImages = avatarTasks.ToDictionary(x => x.Result.AvatarId, x => x.Result);
        var bossImage = bossImageTask.Result;
        var buffImage = buffImageTask.Result;
        var medalImage = medalImageTask.Result;

        background.Mutate(ctx =>
        {
            ctx.Paint(canvas =>
            {
                canvas.DrawText(new RichTextOptions(Fonts.Title)
                {
                    Origin = new Vector2(50, 80),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, "Anomaly Arbitration", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(50, 110),
                    VerticalAlignment = VerticalAlignment.Bottom
                },
                    $"Version {bestRecord.Group.GameVersion}",
                    Brushes.Solid(Color.White), null);

                canvas.DrawImage(medalImage, medalImage.Bounds,
                    new RectangleF(465, 20, medalImage.Width, medalImage.Height), KnownResamplers.Bicubic);
                RichTextOptions bossStarTextOptions = new(Fonts.Title)
                {
                    Origin = new Vector2(600, 100),
                    VerticalAlignment = VerticalAlignment.Bottom
                };
                var bossStarBounds = TextMeasurer.MeasureBounds(bestRecord.BossStars.ToString(), bossStarTextOptions);
                canvas.DrawText(bossStarTextOptions, bestRecord.BossStars.ToString(), Brushes.Solid(Color.White), null);
                canvas.DrawImage(m_BossStarLit, m_BossStarLit.Bounds,
                    new RectangleF((int)bossStarBounds.Right + 5, 50, m_BossStarLit.Width, m_BossStarLit.Height),
                    KnownResamplers.Bicubic);

                RichTextOptions mobStarTextOptions = new(Fonts.Title)
                {
                    Origin = new Vector2(bossStarBounds.Right + m_BossStarLit.Width + 25, 100),
                    VerticalAlignment = VerticalAlignment.Bottom
                };
                canvas.DrawText(mobStarTextOptions, bestRecord.MobStars.ToString(), Brushes.Solid(Color.White), null);
                canvas.DrawImage(m_StarLit, m_StarLit.Bounds,
                    new RectangleF(
                        (int)TextMeasurer.MeasureBounds(bestRecord.MobStars.ToString(), mobStarTextOptions).Right + 5,
                        50, m_StarLit.Width, m_StarLit.Height),
                    KnownResamplers.Bicubic);

                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(1300, 80),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom
                },
                    $"{context.GameProfile.Nickname} • TB {context.GameProfile.Level}",
                    Brushes.Solid(Color.White), null);

                var yOffset = 150;
                DrawBossImage(canvas, new Point(100, yOffset), bestRecord.BossRecord, bestRecord.BossInfo, bossImage, buffImage, avatarImages);

                foreach (var mobRecord in bestRecord.MobRecords.OrderBy(x => x.MazeId))
                {
                    yOffset += 350;
                    var mobInfo = bestRecord.MobInfo.First(x => x.MazeId == mobRecord.MazeId);
                    DrawMobImage(canvas, new Point(225, yOffset), mobRecord, mobInfo, avatarImages);
                }

                canvas.DrawAttribution(new RichTextOptions(Fonts.Tiny)
                {
                    Origin = new Vector2(background.Width - 30, background.Height - 30),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    TextAlignment = TextAlignment.End
                }, new AttributionStyle(TextColor: Color.White));
            });
        });
    }

    private void DrawBossImage(DrawingCanvas canvas, Point position,
        BossRecord? record, BossInfo bossInfo, Image bossImage, Image? buffImage,
        Dictionary<int, HsrAvatar> avatarLookup)
    {
        using var region = canvas.CreateRegion(new Rectangle(position, new Size(1150, 300)));
        _ = region.Save(ClipOptions, new RoundedRectanglePolygon(new RectangleF(Point.Empty, new Size(1150, 300)), 15));
        region.Fill(Brushes.Solid(OverlayColor));
        region.DrawImage(bossImage, bossImage.Bounds, new RectangleF(0, 0, bossImage.Width, bossImage.Height), KnownResamplers.Bicubic);
        region.Restore();

        region.DrawText(new RichTextOptions(Fonts.Normal)
        {
            Origin = new Vector2(300, 43),
            VerticalAlignment = VerticalAlignment.Center,
        }, $"{bossInfo.Name}", Brushes.Solid(Color.White), null);
        for (var i = 0; i < 3; i++)
        {
            var starImage = i < record?.StarNum ? m_BossStarLit : m_BossStarUnlit;
            region.DrawImage(starImage, starImage.Bounds,
                new RectangleF(975 + i * 50, 15, starImage.Width, starImage.Height), KnownResamplers.Bicubic);
        }

        if (record != null)
        {
            RosterImageBuilder.Draw(
                record.Avatars.Select(x => avatarLookup[x.Id]),
                new RosterLayout(MaxSlots: 4),
                new Point(330, 90),
                (point, avatar) => avatar.DrawStyledAvatarImage(region, point));

            region.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(900, 30),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top
            }, record.RoundNum.ToString(), Brushes.Solid(Color.White), null);
            region.DrawImage(m_CycleIcon, m_CycleIcon.Bounds,
                new RectangleF(900, 20, m_CycleIcon.Width, m_CycleIcon.Height), KnownResamplers.Bicubic);

            region.DrawCenteredIcon(buffImage!, new PointF(1055, 170), 55, 0, Color.Black, Color.White);
        }
        else
        {
            region.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(550, 170),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            }, "No Clear Records", Brushes.Solid(Color.White), null);
        }
    }

    private void DrawMobImage(DrawingCanvas canvas, Point position, MobRecord record, MobInfo floorInfo,
        Dictionary<int, HsrAvatar> avatarLookup)
    {
        using var region = canvas.CreateRegion(new Rectangle(position, new Size(900, 300)));
        region.Fill(Brushes.Solid(OverlayColor), new RoundedRectanglePolygon(new RectangleF(Point.Empty, new Size(900, 300)), 15));

        var text = $"{floorInfo.Name} : {floorInfo.MonsterName}";
        region.DrawText(new RichTextOptions(text.Length > 50 ? Fonts.Small! : Fonts.Normal)
        {
            Origin = new Vector2(30, 43),
            VerticalAlignment = VerticalAlignment.Center,
            WrappingLength = 600
        }, text, Brushes.Solid(Color.White), null);

        for (var i = 0; i < 3; i++)
        {
            var starImage = i < record.StarNum ? m_StarLit : m_StarUnlit;
            region.DrawImage(starImage, starImage.Bounds,
                new RectangleF(725 + i * 50, 15, starImage.Width, starImage.Height), KnownResamplers.Bicubic);
        }

        if (record.Avatars.Count == 0)
        {
            region.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(450, 170),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            }, record.IsFast ? "Quick Clear" : "No Clear Records", Brushes.Solid(Color.White), null);
        }
        else
        {
            RosterImageBuilder.Draw(
                record.Avatars.Select(x => avatarLookup[x.Id]),
                new RosterLayout(MaxSlots: 4),
                new Point(125, 90),
                (point, avatar) => avatar.DrawStyledAvatarImage(region, point));
            region.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(650, 30),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top
            }, record.RoundNum.ToString(), Brushes.Solid(Color.White), null);
            region.DrawImage(m_CycleIcon, m_CycleIcon.Bounds,
                new RectangleF(650, 20, m_CycleIcon.Width, m_CycleIcon.Height), KnownResamplers.Bicubic);
        }
    }
}

