#region

using System.Numerics;
using Mehrak.Application.Renderers.Extensions;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Renderers;
using Mehrak.Application.Shared.Renderers.Extensions;
using Mehrak.Application.Shared.Utility;
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
        m_StarLit = await Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hsr.MoCStarName, cancellationToken),
            cancellationToken);
        m_StarUnlit = m_StarLit.Clone(ctx =>
        {
            ctx.Grayscale();
            ctx.Brightness(0.7f);
        });

        m_BossStarLit = await Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hsr.AnomalyStarName, cancellationToken),
            cancellationToken);
        m_BossStarUnlit = m_BossStarLit.Clone(ctx =>
        {
            ctx.Grayscale();
            ctx.Brightness(0.7f);
        });

        m_CycleIcon = await Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hsr.HourglassName, cancellationToken),
            cancellationToken);

        StaticBackground = await Image.LoadAsync<Rgba32>(
            await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hsr.AABackgroundName, cancellationToken),
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
                var bossClear = GetBossImage(bestRecord.BossRecord, bestRecord.BossInfo, bossImage, buffImage, lookup);
                disposables.Add(bossClear);
                canvas.DrawImage(bossClear, bossClear.Bounds,
                    new RectangleF(100, yOffset, bossClear.Width, bossClear.Height), KnownResamplers.Bicubic);

                foreach (var mobRecord in bestRecord.MobRecords.OrderBy(x => x.MazeId))
                {
                    yOffset += 350;
                    var mobInfo = bestRecord.MobInfo.First(x => x.MazeId == mobRecord.MazeId);
                    var mobClear = GetMobImage(mobRecord, mobInfo, lookup);
                    disposables.Add(mobClear);
                    canvas.DrawImage(mobClear, mobClear.Bounds,
                        new RectangleF(225, yOffset, mobClear.Width, mobClear.Height), KnownResamplers.Bicubic);
                }
            });
        });
    }

    private Image<Rgba32> GetBossImage(BossRecord? record, BossInfo bossInfo,
        Image bossImage, Image? buffImage,
        Dictionary<HsrAvatar, Image<Rgba32>>.AlternateLookup<int> avatarLookup)
    {
        Image<Rgba32> image = new(1150, 300);
        image.Mutate(ctx =>
        {
            ctx.Paint(canvas =>
            {
                canvas.Fill(Brushes.Solid(OverlayColor), new Rectangle(0, 0, image.Width, image.Height));
            });

            ctx.Paint(canvas =>
            {
                canvas.DrawImage(bossImage, bossImage.Bounds,
                    new RectangleF(0, 0, bossImage.Width, bossImage.Height), KnownResamplers.Bicubic);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(300, 43),
                    VerticalAlignment = VerticalAlignment.Center,
                }, $"{bossInfo.Name}", Brushes.Solid(Color.White), null);
                for (var i = 0; i < 3; i++)
                {
                    var starImage = i < record?.StarNum ? m_BossStarLit : m_BossStarUnlit;
                    canvas.DrawImage(starImage, starImage.Bounds,
                        new RectangleF(975 + i * 50, 15, starImage.Width, starImage.Height), KnownResamplers.Bicubic);
                }

                if (record != null)
                {
                    using var rosterImage = RosterImageBuilder.Build(
                        record.Avatars.Select(x => avatarLookup[x.Id]),
                        new RosterLayout(MaxSlots: 4));
                    canvas.DrawImage(rosterImage, rosterImage.Bounds,
                        new RectangleF(330, 90, rosterImage.Width, rosterImage.Height), KnownResamplers.Bicubic);

                    canvas.DrawText(new RichTextOptions(Fonts.Normal)
                    {
                        Origin = new Vector2(900, 30),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Top
                    }, record.RoundNum.ToString(), Brushes.Solid(Color.White), null);
                    canvas.DrawImage(m_CycleIcon, m_CycleIcon.Bounds,
                        new RectangleF(900, 20, m_CycleIcon.Width, m_CycleIcon.Height), KnownResamplers.Bicubic);

                    canvas.DrawCenteredIcon(buffImage!, new PointF(1055, 170), 55, 0, Color.Black, Color.White);
                }
                else
                {
                    canvas.DrawText(new RichTextOptions(Fonts.Normal)
                    {
                        Origin = new Vector2(550, 170),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }, "No Clear Records", Brushes.Solid(Color.White), null);
                }
            });

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
            ctx.Paint(canvas =>
            {
                canvas.Fill(Brushes.Solid(OverlayColor), new Rectangle(0, 0, image.Width, image.Height));
            });

            ctx.Paint(canvas =>
            {
                var text = $"{floorInfo.Name} : {floorInfo.MonsterName}";
                canvas.DrawText(new RichTextOptions(text.Length > 50 ? Fonts.Small! : Fonts.Normal)
                {
                    Origin = new Vector2(30, 43),
                    VerticalAlignment = VerticalAlignment.Center,
                    WrappingLength = 600
                }, text, Brushes.Solid(Color.White), null);

                for (var i = 0; i < 3; i++)
                {
                    var starImage = i < record.StarNum ? m_StarLit : m_StarUnlit;
                    canvas.DrawImage(starImage, starImage.Bounds,
                        new RectangleF(725 + i * 50, 15, starImage.Width, starImage.Height), KnownResamplers.Bicubic);
                }

                if (record.Avatars.Count == 0)
                {
                    canvas.DrawText(new RichTextOptions(Fonts.Normal)
                    {
                        Origin = new Vector2(450, 170),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }, record.IsFast ? "Quick Clear" : "No Clear Records", Brushes.Solid(Color.White), null);
                }
                else
                {
                    using var rosterImage = RosterImageBuilder.Build(
                        record.Avatars.Select(x => avatarLookup[x.Id]),
                        new RosterLayout(MaxSlots: 4));
                    canvas.DrawImage(rosterImage, rosterImage.Bounds,
                        new RectangleF(125, 90, rosterImage.Width, rosterImage.Height), KnownResamplers.Bicubic);
                    canvas.DrawText(new RichTextOptions(Fonts.Normal)
                    {
                        Origin = new Vector2(650, 30),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Top
                    }, record.RoundNum.ToString(), Brushes.Solid(Color.White), null);
                    canvas.DrawImage(m_CycleIcon, m_CycleIcon.Bounds,
                        new RectangleF(650, 20, m_CycleIcon.Width, m_CycleIcon.Height), KnownResamplers.Bicubic);
                }
            });

            ctx.ApplyRoundedCorners(15);
        });
        return image;
    }
}
