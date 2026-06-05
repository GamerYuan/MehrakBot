#region

using System.Numerics;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Renderers;
using Mehrak.Application.Shared.Renderers.Extensions;
using Mehrak.Application.Shared.Utility;
using Mehrak.Application.Zzz;
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
            .ToDictionaryAsync(x => x.AvatarId, x => x, cancellationToken: cancellationToken);
        var buddyImages = await data.FifthLayerDetail.LayerChallengeInfoList
            .Select(x => x.Buddy)
            .Where(x => x is not null)
            .DistinctBy(x => x!.Id)
            .ToAsyncEnumerable()
            .ToDictionaryAsync((x, token) => ValueTask.FromResult(x!.Id),
                async (x, token) =>
                {
                    var buddyImg = await LoadImageFromRepositoryAsync(x!.ToImageName(), disposables, token);
                    buddyImg.Mutate(ctx => ctx.Transform(new AffineTransformBuilder().AppendTranslation(new PointF(-45, 0))));
                    return buddyImg;
                }, cancellationToken: cancellationToken);
        var bossImages = await data.FifthLayerDetail.LayerChallengeInfoList
            .ToAsyncEnumerable()
            .ToDictionaryAsync((x, token) => ValueTask.FromResult(x.LayerId),
                async (x, token) => await LoadImageFromRepositoryAsync(
                    x.ToMonsterImageName(), disposables, token), cancellationToken: cancellationToken);

        var tzi = context.GetParameter<Server>("server").GetTimeZoneInfo();

        background.Mutate(ctx =>
        {
            ctx.Resize(new ResizeOptions
            {
                CenterCoordinates = new PointF(ctx.GetCurrentSize().Width / 2f, ctx.GetCurrentSize().Height / 2f),
                Size = new Size(1000, 1080),
                Mode = ResizeMode.Crop,
                Sampler = KnownResamplers.Bicubic
            });

            var imageSize = ctx.GetCurrentSize();

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
                DrawRankIcon(canvas, new Point(15 + (int)totalScoreBounds.Right, 135), data.Brief);
                var ratingImg = m_RatingImages[data.Brief.Rating];
                canvas.DrawImage(ratingImg, ratingImg.Bounds,
                    new RectangleF(850, 140, ratingImg.Width, ratingImg.Height), KnownResamplers.Bicubic);

                var i = 0;
                foreach (var floor in data.FifthLayerDetail.LayerChallengeInfoList)
                {
                    DrawFloorImage(canvas, new Point(50, 220 + i * 270), floor, avatarImages, bossImages[floor.LayerId],
                        floor.Buddy == null ? null : buddyImages[floor.Buddy.Id]);
                    i++;
                }

                canvas.DrawAttribution(new RichTextOptions(Fonts.Tiny)
                {
                    Origin = new PointF(imageSize.Width - 20, imageSize.Height - 20),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    TextAlignment = TextAlignment.End,
                }
                );
            });
        });
    }

    private void DrawRankIcon(DrawingCanvas canvas, Point location, HadalBrief brief)
    {
        var image = m_RankIcons.First(x => brief.RankPercent <= x.Boundary).Icon;
        _ = canvas.SaveLayer();
        canvas.DrawImage(image, image.Bounds,
            new RectangleF(location.X, location.Y, image.Width, image.Height), KnownResamplers.Bicubic);

        var rankText = $"{(float)brief.RankPercent / 100:N2}%";
        var size = TextMeasurer.MeasureBounds(rankText, new TextOptions(Fonts.Small));

        _ = canvas.Save(new DrawingOptions { GraphicsOptions = new GraphicsOptions { AlphaCompositionMode = PixelAlphaCompositionMode.Xor } });
        canvas.DrawText(new RichTextOptions(size.Width <= 80 ? Fonts.Small : Fonts.Tiny!)
        {
            Origin = new PointF(location.X + 8, location.Y + 12),
        }, rankText, Brushes.Solid(Color.White), null);
        canvas.Restore();
        canvas.Restore();
    }

    private void DrawFloorImage(
        DrawingCanvas canvas, Point location, HadalChallengeInfo floor,
        Dictionary<int, ZzzAvatar> avatarLookup,
        Image bossImage,
        Image? buddyImage)
    {
        using var region = canvas.CreateRegion(new Rectangle(location.X, location.Y, 900, 260));
        _ = region.Save(ClipOptions, new RoundedRectanglePolygon(0, 0, 900, 260, 15));
        region.Fill(Brushes.Solid(LocalOverlayColor));
        region.DrawImage(bossImage, bossImage.Bounds,
            new RectangleF(0, 0, bossImage.Width, bossImage.Height), KnownResamplers.Bicubic);
        region.Restore();

        region.DrawText(new RichTextOptions(Fonts.Normal)
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Origin = new Vector2(800, 15)
        }, floor.Score.ToString(), Brushes.Solid(Color.White), null);
        region.DrawText(new RichTextOptions(Fonts.Normal)
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Origin = new Vector2(680, 15)
        }, $"{floor.BattleTime}s", Brushes.Solid(Color.White), null);
        var ratingImg = m_SmallRatingImages[floor.Rating];
        region.DrawImage(ratingImg, ratingImg.Bounds,
            new RectangleF(800, 10, ratingImg.Width, ratingImg.Height), KnownResamplers.Bicubic);

        object?[] roster = [.. floor.AvatarList.Select(x => avatarLookup[x.Id]), buddyImage];

        RosterImageBuilder.Draw(
            roster,
            new RosterLayout(MaxSlots: 4),
            new Point(220, 60),
            (point, item) =>
            {
                switch (item)
                {
                    case ZzzAvatar avatar:
                        avatar.DrawStyledAvatarImage(region, point);
                        break;
                    default:
                        var buddyImg = item as Image ?? m_BaseBuddyImage;
                        AvatarImageUtility.DrawStyledBuddyImage(region, point, buddyImg);
                        break;
                }
            });
    }


}
