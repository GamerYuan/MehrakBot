#region

using System.Numerics;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Renderers;
using Mehrak.Application.Shared.Renderers.Extensions;
using Mehrak.Application.Shared.Utility;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Models;
using Mehrak.Domain.Shared.Common;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Utility;
using Mehrak.Domain.User.Abstractions;
using Mehrak.GameApi.Zzz.Types;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Zzz.Defense;

internal class ZzzDefenseCardService : CardServiceBase<ZzzDefenseDataV2>
{
    private readonly List<(int Boundary, Image Icon)> m_RankIcons = [];
    private Dictionary<string, Image> m_RatingImages = [];
    private Dictionary<string, Image> m_SmallRatingImages = [];
    private Image m_BaseBuddyImage = null!;

    private static readonly DrawingOptions RankIconTextDrawingOptions = new()
    {
        GraphicsOptions = new GraphicsOptions()
        {
            AlphaCompositionMode = PixelAlphaCompositionMode.Xor
        }
    };

    private static readonly Color LocalOverlayColor = Color.FromPixel(new Rgba32(0, 0, 0, 128));

    public ZzzDefenseCardService(IImageRepository imageRepository,
        ILogger<ZzzDefenseCardService> logger,
        IApplicationMetrics metrics)
        : base(
            "Zzz Shiyu",
            imageRepository,
            logger,
            metrics,
            LoadFonts("Assets/Fonts/zzz.ttf", titleSize: 40, normalSize: 28, smallSize: 20, tinySize: 18))
    {
    }

    public override async Task LoadStaticResourcesAsync(CancellationToken cancellationToken = default)
    {
        string[] rating = ["S+", "S", "A", "B"];
        m_RatingImages = await rating.ToAsyncEnumerable()
            .Select(async (x, token) =>
            {
                var image = await Image.LoadAsync(
                    await ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.Zzz.RatingName, x), token), token);
                image.Mutate(ctx => ctx.Resize(80, 0));
                return (Rating: x, Image: image);
            })
            .ToDictionaryAsync(x => x.Rating, x => x.Image, cancellationToken: cancellationToken);
        m_SmallRatingImages = m_RatingImages.Select(x => (x.Key, x.Value.Clone(y => y.Resize(0, 40))))
            .ToDictionary();
        m_BaseBuddyImage = await Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.Zzz.BuddyName, "base"), cancellationToken),
            cancellationToken);

        StaticBackground = await Image.LoadAsync<Rgba32>(
            await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Zzz.ShiyuBackgroundName, cancellationToken),
            cancellationToken);

        m_RankIcons.Add((199, await Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.Zzz.RankBackgroundName, 1), cancellationToken),
            cancellationToken)));
        m_RankIcons.Add((299, await Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.Zzz.RankBackgroundName, 2), cancellationToken),
            cancellationToken)));
        m_RankIcons.Add((599, await Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.Zzz.RankBackgroundName, 3), cancellationToken),
            cancellationToken)));
        m_RankIcons.Add((2099, await Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.Zzz.RankBackgroundName, 4), cancellationToken),
            cancellationToken)));
        m_RankIcons.Add((int.MaxValue, await Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.Zzz.RankBackgroundName, 5), cancellationToken),
            cancellationToken)));
    }

    protected override Image<Rgba32> CreateBackground()
    {
        return StaticBackground!.CloneAs<Rgba32>();
    }

    public override async Task RenderCardAsync(
        Image<Rgba32> background,
        ICardGenerationContext<ZzzDefenseDataV2> context,
        DisposableBag disposables,
        CancellationToken cancellationToken = default)
    {
        var data = context.Data;

        if (data.FifthLayerDetail == null || data.Brief == null)
        {
            Logger.LogInformation(LogMessage.NoClearRecords, "Defense", context.UserId, context.GameProfile.GameUid);
            throw new CommandException("No clear records found for Defense");
        }

        var avatarImages = await data.FifthLayerDetail.LayerChallengeInfoList
            .SelectMany(x => x.AvatarList)
            .DistinctBy(x => x.Id)
            .ToAsyncEnumerable()
            .Select(async (x, token) =>
            {
                await using var stream = await ImageRepository.DownloadFileToStreamAsync(x.ToImageName(), token);
                var avatar = new ZzzAvatar(x.Id, x.Level, x.Rarity[0], x.Rank, await Image.LoadAsync(stream, token));
                disposables.Add(avatar);
                return avatar;
            })
            .ToDictionaryAsync(x => x,
                x =>
                {
                    var styledImage = x.GetStyledAvatarImage();
                    disposables.Add(styledImage);
                    return styledImage;
                }, ZzzAvatarIdComparer.Instance, cancellationToken: cancellationToken);
        var buddyImages = await data.FifthLayerDetail.LayerChallengeInfoList
            .Select(x => x.Buddy)
            .Where(x => x is not null)
            .DistinctBy(x => x!.Id)
            .ToAsyncEnumerable()
            .ToDictionaryAsync((x, token) => ValueTask.FromResult(x!.Id),
                async (x, token) =>
                    await LoadImageFromRepositoryAsync(x!.ToImageName(), disposables, token), cancellationToken: cancellationToken);
        var bossImages = await data.FifthLayerDetail.LayerChallengeInfoList
            .ToAsyncEnumerable()
            .ToDictionaryAsync((x, token) => ValueTask.FromResult(x.LayerId),
                async (x, token) => await LoadImageFromRepositoryAsync(
                    x.ToMonsterImageName(), disposables, token), cancellationToken: cancellationToken);

        var lookup = avatarImages.GetAlternateLookup<int>();

        background.Mutate(ctx =>
        {
            ctx.Resize(new ResizeOptions
            {
                CenterCoordinates = new PointF(ctx.GetCurrentSize().Width / 2f, ctx.GetCurrentSize().Height / 2f),
                Size = new Size(1000, 1050),
                Mode = ResizeMode.Crop,
                Sampler = KnownResamplers.Bicubic
            });
        });

        var tzi = context.GetParameter<Server>("server").GetTimeZoneInfo();

        background.Mutate(ctx =>
        {
            ctx.Paint(canvas =>
            {
                canvas.DrawText(new RichTextOptions(Fonts.Title)
                {
                    Origin = new Vector2(50, 70),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, "Shiyu Defense", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(50, 100),
                    VerticalAlignment = VerticalAlignment.Bottom
                },
                    $"{DateTimeOffset.FromUnixTimeSeconds(long.Parse(data.BeginTime))
                        .ToOffset(tzi.BaseUtcOffset):dd/MM/yyyy} - " +
                    $"{DateTimeOffset.FromUnixTimeSeconds(long.Parse(data.EndTime))
                        .ToOffset(tzi.BaseUtcOffset):dd/MM/yyyy}",
                    Brushes.Solid(Color.White), null);

                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(950, 70),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Right
                }, $"{context.GameProfile.Nickname} · IK {context.GameProfile.Level}", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(950, 100),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Right
                },
                    $"{context.GameProfile.GameUid}", Brushes.Solid(Color.White), null);

                canvas.DrawRoundedRectangleOverlay(900, 80, new PointF(50, 120),
                    new RoundedRectangleOverlayStyle(LocalOverlayColor, CornerRadius: 15));

                var totalScoreText = $"Total Score: {data.Brief.Score}";
                var totalScoreTextOptions = new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(70, 145),
                    VerticalAlignment = VerticalAlignment.Top
                };
                var totalScoreBounds =
                    TextMeasurer.MeasureBounds(totalScoreText, totalScoreTextOptions);

                canvas.DrawText(totalScoreTextOptions, totalScoreText, Brushes.Solid(Color.White), null);
                using var rankIcon = GetRankIcon(data.Brief);
                canvas.DrawImage(rankIcon, rankIcon.Bounds,
                    new RectangleF(15 + (int)totalScoreBounds.Right, 135, rankIcon.Width, rankIcon.Height),
                    KnownResamplers.Bicubic);
                var ratingImg = m_RatingImages[data.Brief.Rating];
                canvas.DrawImage(ratingImg, ratingImg.Bounds,
                    new RectangleF(850, 140, ratingImg.Width, ratingImg.Height), KnownResamplers.Bicubic);

                var i = 0;
                foreach (var floor in data.FifthLayerDetail.LayerChallengeInfoList)
                {
                    var floorImage = GetFloorImage(floor, lookup, bossImages[floor.LayerId],
                        floor.Buddy is not null ? buddyImages[floor.Buddy!.Id] : null,
                        disposables);
                    disposables.Add(floorImage);
                    canvas.DrawImage(floorImage, floorImage.Bounds,
                        new RectangleF(50, 220 + i * 270, floorImage.Width, floorImage.Height),
                        KnownResamplers.Bicubic);
                    i++;
                }
            });
        });
    }

    private Image<Rgba32> GetRankIcon(HadalBrief brief)
    {
        var image = m_RankIcons.First(x => brief.RankPercent <= x.Boundary).Icon.CloneAs<Rgba32>();
        image.Mutate(ctx =>
        {
            ctx.Paint(canvas =>
            {
                var rankText = $"{(float)brief.RankPercent / 100:N2}%";
                var size = TextMeasurer.MeasureBounds(rankText, new TextOptions(Fonts.Small));
                canvas.Save(new DrawingOptions
                {
                    GraphicsOptions = new GraphicsOptions
                    {
                        AlphaCompositionMode = PixelAlphaCompositionMode.Xor
                    }
                });
                canvas.DrawText(new RichTextOptions(size.Width <= 80 ? Fonts.Small : Fonts.Tiny!)
                {
                    Origin = new PointF(8, 12),
                }, rankText, Brushes.Solid(Color.White), null);
                canvas.Restore();
            });
        });
        return image;
    }

    private Image<Rgba32> GetFloorImage(HadalChallengeInfo floor,
        Dictionary<ZzzAvatar, Image<Rgba32>>.AlternateLookup<int> avatarLookup,
        Image bossImage,
        Image? buddyImage,
        DisposableBag disposables)
    {
        Image<Rgba32> image = new(900, 260);
        image.Mutate(ctx =>
        {
            ctx.Paint(canvas => canvas.Fill(Brushes.Solid(LocalOverlayColor), new Rectangle(0, 0, image.Width, image.Height)));

            ctx.Paint(canvas =>
            {
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Origin = new Vector2(800, 15)
                }, floor.Score.ToString(), Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Origin = new Vector2(680, 15)
                }, $"{floor.BattleTime}s", Brushes.Solid(Color.White), null);
                var ratingImg = m_SmallRatingImages[floor.Rating];
                canvas.DrawImage(ratingImg, ratingImg.Bounds,
                    new RectangleF(800, 10, ratingImg.Width, ratingImg.Height), KnownResamplers.Bicubic);

                canvas.DrawImage(bossImage, bossImage.Bounds,
                    new RectangleF(0, 0, bossImage.Width, bossImage.Height), KnownResamplers.Bicubic);
                var styledBuddy = GetStyledBuddyImage(buddyImage);
                disposables.Add(styledBuddy);
                using var rosterImage = RosterImageBuilder.Build(
                    floor.AvatarList.Select(x => avatarLookup[x.Id]),
                    new RosterLayout(MaxSlots: 4),
                    styledBuddy);
                canvas.DrawImage(rosterImage, rosterImage.Bounds,
                    new RectangleF(220, 60, rosterImage.Width, rosterImage.Height), KnownResamplers.Bicubic);
            });

            ctx.ApplyRoundedCorners(15);
        });
        return image;
    }

    private Image<Rgba32> GetStyledBuddyImage(Image? buddyImage)
    {
        Image<Rgba32> buddyBorder = new(150, 180);
        buddyBorder.Mutate(x =>
        {
            var outerPath = ImageUtility.CreateRoundedRectanglePath(150, 180, 15);
            x.Paint(canvas => canvas.Fill(Brushes.Solid(Color.FromPixel(new Rgb24(24, 24, 24))), new Rectangle(0, 0, buddyBorder.Width, buddyBorder.Height)));

            x.Paint(canvas =>
            {
                var buddySrc = buddyImage ?? m_BaseBuddyImage;
                canvas.DrawImage(buddySrc, buddySrc.Bounds,
                    new RectangleF(-45, 0, buddySrc.Width, buddySrc.Height), KnownResamplers.Bicubic);
                canvas.Draw(Pens.Solid(Color.Black, 4f), outerPath);
            });

            x.ApplyRoundedCorners(15);
        });
        return buddyBorder;
    }
}
