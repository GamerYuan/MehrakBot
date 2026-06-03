#region

using System.Numerics;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Renderers;
using Mehrak.Application.Shared.Renderers.Extensions;
using Mehrak.Application.Shared.Utility;
using Mehrak.Application.Zzz;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Models;
using Mehrak.Domain.User.Abstractions;
using Mehrak.GameApi.Zzz.Types;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Zzz.Assault;

internal class ZzzAssaultCardService : CardServiceBase<ZzzAssaultData>
{
    private Image m_StarLitImage = null!;
    private Image m_StarLitSmall = null!;
    private Image m_StarUnlitSmall = null!;
    private Image m_BaseBuddyImage = null!;

    private static readonly Color BackgroundColor = Color.FromPixel(new Rgb24(30, 30, 30));
    private static readonly Color LocalOverlayColor = Color.FromPixel(new Rgb24(69, 69, 69));

    public ZzzAssaultCardService(IImageRepository imageRepository,
        ILogger<ZzzAssaultCardService> logger,
        IApplicationMetrics metrics)
        : base(
            "Zzz DA",
            imageRepository,
            logger,
            metrics,
            LoadFonts("Assets/Fonts/zzz.ttf", titleSize: 40, normalSize: 28, smallSize: 20))
    {
    }

    public override async Task LoadStaticResourcesAsync(CancellationToken cancellationToken = default)
    {
        m_StarLitImage = await Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Zzz.AssaultStarName, cancellationToken),
            cancellationToken);
        m_StarLitSmall = m_StarLitImage.Clone(ctx => ctx.Resize(0, 35));
        m_StarUnlitSmall = m_StarLitImage.Clone(ctx =>
        {
            ctx.Grayscale();
            ctx.Brightness(0.5f);
            ctx.Resize(0, 35);
        });

        m_BaseBuddyImage = await Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.Zzz.BuddyName, "base"), cancellationToken),
            cancellationToken);
        m_BaseBuddyImage.Mutate(ctx => ctx.Transform(new AffineTransformBuilder().AppendTranslation(new PointF(-45, 0))));
    }

    protected override Image<Rgba32> CreateBackground()
    {
        return new Image<Rgba32>(1, 1);
    }

    public override async Task RenderCardAsync(
        Image<Rgba32> background,
        ICardGenerationContext<ZzzAssaultData> context,
        DisposableBag disposables,
        CancellationToken cancellationToken = default)
    {
        var data = context.Data;
        var height = data.List.Count * 270 + 200;

        var avatarImages = await data.List.SelectMany(x => x.AvatarList)
            .DistinctBy(x => x.Id)
            .ToAsyncEnumerable()
            .Select(async (x, token) =>
            {
                await using var stream = await ImageRepository.DownloadFileToStreamAsync(x.ToImageName(), token);
                var image = await Image.LoadAsync(stream, token);
                ZzzAvatar avatar = new(x.Id, x.Level, x.Rarity[0], x.Rank, image);
                return avatar;
            })
            .ToDictionaryAsync(x => x.AvatarId, x => x, cancellationToken: cancellationToken);

        var buddyImages = await data.List.Select(x => x.Buddy)
            .Where(x => x is not null)
            .DistinctBy(x => x!.Id)
            .ToAsyncEnumerable()
            .Select(async (x, token) =>
            {
                await using var stream = await ImageRepository.DownloadFileToStreamAsync(x!.ToImageName(), token);
                var image = await Image.LoadAsync(stream, token);
                disposables.Add(image);
                image.Mutate(ctx => ctx.Transform(new AffineTransformBuilder().AppendTranslation(new PointF(-45, 0))));
                return (BuddyId: x!.Id, Image: image);
            })
            .ToDictionaryAsync(x => x.BuddyId, x => x.Image, cancellationToken: cancellationToken);

        var bossImages = await data.List.SelectMany(x => x.Boss)
            .ToAsyncEnumerable()
            .ToDictionaryAsync(
                (x, token) => ValueTask.FromResult(x.Name),
                async (x, token) => await LoadImageFromRepositoryAsync(x.ToImageName(), disposables, token),
                cancellationToken: cancellationToken);

        var buffImages = await data.List.SelectMany(x => x.Buff)
            .DistinctBy(x => x.Name)
            .ToAsyncEnumerable()
            .ToDictionaryAsync((x, token) => ValueTask.FromResult(x.Name),
                async (x, token) => await LoadImageFromRepositoryAsync(x.ToImageName(), disposables, token),
                cancellationToken: cancellationToken);

        background.Mutate(ctx => ctx.Resize(1050, height));

        background.Mutate(ctx =>
        {
            ctx.Paint(canvas => canvas.Fill(Brushes.Solid(BackgroundColor), new Rectangle(0, 0, background.Width, background.Height)));

            ctx.Paint(canvas =>
            {
                canvas.DrawText(new RichTextOptions(Fonts.Title)
                {
                    Origin = new Vector2(50, 70),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, "Deadly Assault", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(50, 100),
                    VerticalAlignment = VerticalAlignment.Bottom
                },
                    $"{data.StartTime.Day}/{data.StartTime.Month}/{data.StartTime.Year} - " +
                    $"{data.EndTime.Day}/{data.EndTime.Month}/{data.EndTime.Year}",
                    Brushes.Solid(Color.White), null);

                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(1000, 70),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Right
                }, $"{context.GameProfile.Nickname} · IK {context.GameProfile.Level}", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(1000, 100),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Right
                },
                    $"{context.GameProfile.GameUid}", Brushes.Solid(Color.White), null);

                var totalScoreText = $"Total Score: {data.TotalScore}";
                var totalScoreBounds =
                    TextMeasurer.MeasureBounds(totalScoreText, new TextOptions(Fonts.Title));

                canvas.DrawText(new RichTextOptions(Fonts.Title)
                {
                    Origin = new Vector2(50, 150),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, totalScoreText, Brushes.Solid(Color.White), null);
                canvas.DrawRoundedRectangleOverlay(90, 40, new PointF(60 + totalScoreBounds.Width, 110),
                    new RoundedRectangleOverlayStyle(LocalOverlayColor, CornerRadius: 15));
                canvas.DrawText(new RichTextOptions(Fonts.Small!)
                {
                    Origin = new Vector2(105 + (int)totalScoreBounds.Width, 140),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Center
                }, $"{(float)data.RankPercent / 100:N2}%", Brushes.Solid(Color.White), null);

                canvas.DrawImage(m_StarLitImage, m_StarLitImage.Bounds,
                    new RectangleF(160 + (int)totalScoreBounds.Width, 100, m_StarLitImage.Width, m_StarLitImage.Height),
                    KnownResamplers.Bicubic);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(210 + (int)totalScoreBounds.Width, 140),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, $"x{data.TotalStar}", Brushes.Solid(Color.White), null);

                for (var i = 0; i < data.List.Count; i++)
                {
                    var floor = data.List[i];
                    var yOffset = 180 + i * 270;
                    DrawFloorImage(canvas, new Point(50, yOffset), floor, avatarImages, bossImages[floor.Boss[0].Name],
                        buffImages[floor.Buff[0].Name],
                        floor.Buddy == null ? null : buddyImages[floor.Buddy.Id]);
                }
            });
        });
    }

    private void DrawFloorImage(
        DrawingCanvas canvas,
        Point position,
        AssaultFloorDetail floor,
        Dictionary<int, ZzzAvatar> avatarLookup,
        Image bossImage,
        Image buffImage,
        Image? buddyImage
    )
    {
        using var region = canvas.CreateRegion(new Rectangle(position, new Size(950, 260)));
        _ = region.Save(ClipOptions, new RoundedRectanglePolygon(new RectangleF(0, 0, 950, 260), 15));
        region.Fill(Brushes.Solid(LocalOverlayColor), new Rectangle(0, 0, 950, 260));
        region.Restore();

        region.DrawText(new RichTextOptions(floor.Boss[0].Name.Length > 25 ? Fonts.Small! : Fonts.Normal)
        {
            Origin = new Vector2(200, 30),
            VerticalAlignment = VerticalAlignment.Center,
            WrappingLength = 500
        }, floor.Boss[0].Name, Brushes.Solid(Color.White), null);
        var scoreText = floor.Score.ToString();
        var scoreBounds = TextMeasurer.MeasureBounds(scoreText, new TextOptions(Fonts.Normal));
        region.DrawText(new RichTextOptions(Fonts.Normal)
        {
            Origin = new Vector2(925, 15),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right
        }, floor.Score.ToString(), Brushes.Solid(Color.White), null);
        for (var i = 2; i >= 0; i--)
        {
            var starImage = i < floor.Star ? m_StarLitSmall : m_StarUnlitSmall;
            region.DrawImage(starImage, starImage.Bounds,
                new RectangleF(885 - (int)scoreBounds.Width - i * 35, 10, starImage.Width, starImage.Height),
                KnownResamplers.Bicubic);
        }

        region.DrawImage(bossImage, bossImage.Bounds,
            new RectangleF(25, 15, bossImage.Width, bossImage.Height),
            KnownResamplers.Bicubic);

        object?[] roster = [.. floor.AvatarList.Select(x => avatarLookup[x.Id]), buddyImage];

        RosterImageBuilder.Draw(
            roster,
            new RosterLayout(MaxSlots: 4),
            new Point(190, 60),
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

        region.DrawImage(buffImage, buffImage.Bounds,
            new RectangleF(850, 110, buffImage.Width, buffImage.Height),
            KnownResamplers.Bicubic);
    }


}
