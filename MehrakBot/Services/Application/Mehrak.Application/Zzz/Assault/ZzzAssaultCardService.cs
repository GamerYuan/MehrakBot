#region

using System.Numerics;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Renderers;
using Mehrak.Application.Shared.Renderers.Extensions;
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
    private Image m_HardStarLitSmall = null!;
    private Image m_HardStarUnlitSmall = null!;
    private Image m_BaseBuddyImage = null!;
    private readonly List<(int Boundary, Image Icon)> m_RankIcons = [];

    private static readonly Color BackgroundColor = Color.FromPixel(new Rgb24(0x10, 0x11, 0x14));

    private static readonly Color NormalGradientStart = Color.FromPixel(new Rgb24(0x3A, 0x23, 0x48));
    private static readonly Color NormalGradientEnd = Color.FromPixel(new Rgb24(0x08, 0x05, 0x0B));
    private static readonly Color HardGradientStart = Color.FromPixel(new Rgb24(0x54, 0x19, 0x23));
    private static readonly Color HardGradientEnd = Color.FromPixel(new Rgb24(0x0B, 0x03, 0x05));

    private static readonly DrawingOptions RankIconTextDrawingOptions = new()
    {
        GraphicsOptions = new GraphicsOptions
        {
            AlphaCompositionMode = PixelAlphaCompositionMode.Xor
        }
    };

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
        var starTask = Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Zzz.AssaultStarName, cancellationToken),
            cancellationToken);
        var hardStarTask = Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Zzz.AssaultHardStarName, cancellationToken),
            cancellationToken);
        var buddyTask = Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.Zzz.BuddyName, "base"), cancellationToken),
            cancellationToken);
        var rankTasks = Enumerable.Range(1, 5)
            .Select(async i => await Image.LoadAsync(
                await ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.Zzz.RankBackgroundName, i), cancellationToken),
                cancellationToken))
            .ToList();

        await Task.WhenAll(starTask, hardStarTask, buddyTask);
        await Task.WhenAll(rankTasks);

        m_StarLitImage = starTask.Result;
        m_StarLitSmall = m_StarLitImage.Clone(ctx => ctx.Resize(0, 35));
        m_StarUnlitSmall = m_StarLitImage.Clone(ctx =>
        {
            ctx.Grayscale();
            ctx.Brightness(0.5f);
            ctx.Resize(0, 35);
        });
        using var hardStarImage = hardStarTask.Result;
        m_HardStarLitSmall = hardStarImage.Clone(ctx => ctx.Resize(0, 45));
        m_HardStarUnlitSmall = m_HardStarLitSmall.Clone(ctx =>
        {
            ctx.Grayscale();
            ctx.Brightness(0.5f);
        });

        int[] boundaries = [199, 299, 599, 2099, int.MaxValue];
        m_RankIcons.AddRange(boundaries.Zip(rankTasks, (boundary, task) => (boundary, task.Result)));

        m_BaseBuddyImage = buddyTask.Result;
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
        var hasHard = data.HasHard && data.HardList.Count > 0;
        var parentY = hasHard ? 490 : 130;
        var height = parentY + 100 + data.List.Count * 270 + 60;

        var allFloors = hasHard ? [.. data.List, .. data.HardList] : data.List;

        var avatarTasks = allFloors.SelectMany(x => x.AvatarList)
            .DistinctBy(x => x.Id)
            .Select(async x =>
            {
                await using var stream = await ImageRepository.DownloadFileToStreamAsync(x.ToImageName(), cancellationToken);
                var image = await Image.LoadAsync(stream, cancellationToken);
                ZzzAvatar avatar = new(x.Id, x.Level, x.Rarity[0], x.Rank, image);
                disposables.Add(avatar);
                return avatar;
            })
            .ToList();

        var buddyTasks = allFloors.Select(x => x.Buddy)
            .Where(x => x is not null)
            .DistinctBy(x => x!.Id)
            .Select(async x =>
            {
                await using var stream = await ImageRepository.DownloadFileToStreamAsync(x!.ToImageName(), cancellationToken);
                var image = await Image.LoadAsync(stream, cancellationToken);
                disposables.Add(image);
                image.Mutate(ctx => ctx.Transform(new AffineTransformBuilder().AppendTranslation(new PointF(-45, 0))));
                return (BuddyId: x!.Id, Image: image);
            })
            .ToList();

        var bossEntries = allFloors.SelectMany(x => x.Boss)
            .DistinctBy(x => x.Name)
            .ToList();
        var bossTasks = bossEntries
            .Select(async x => await LoadImageFromRepositoryAsync(x.ToImageName(), disposables, cancellationToken))
            .ToList();

        var buffEntries = allFloors.SelectMany(x => x.Buff)
            .DistinctBy(x => x.Name)
            .ToList();
        var buffTasks = buffEntries
            .Select(async x => await LoadImageFromRepositoryAsync(x.ToImageName(), disposables, cancellationToken))
            .ToList();

        await Task.WhenAll(avatarTasks.Cast<Task>()
            .Concat(buddyTasks.Cast<Task>())
            .Concat(bossTasks.Cast<Task>())
            .Concat(buffTasks.Cast<Task>()));

        var avatarImages = avatarTasks.ToDictionary(x => x.Result.AvatarId, x => x.Result);
        var buddyImages = buddyTasks.ToDictionary(x => x.Result.BuddyId, x => x.Result.Image);
        var bossImages = bossEntries.Zip(bossTasks, (entry, task) => (entry.Name, task.Result))
            .ToDictionary(x => x.Name, x => x.Result);
        var buffImages = buffEntries.Zip(buffTasks, (entry, task) => (entry.Name, task.Result))
            .ToDictionary(x => x.Name, x => x.Result);

        background.Mutate(ctx =>
        {
            ctx.Resize(1050, height);
            var imageSize = ctx.GetCurrentSize();

            ctx.Paint(canvas =>
            {
                canvas.Clear(Brushes.Solid(BackgroundColor));
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

                if (hasHard)
                {
                    var hardFloor = data.HardList[0];
                    DrawFloorImage(canvas, new Point(35, 130), 980, 330, hardFloor, avatarImages,
                        bossImages[hardFloor.Boss[0].Name], buffImages[hardFloor.Buff[0].Name],
                        hardFloor.Buddy == null ? null : buddyImages[hardFloor.Buddy.Id],
                        hard: true, rankPercent: data.HardRankPercent);
                }

                canvas.Fill(CreateGradient(35, 980, NormalGradientStart, NormalGradientEnd),
                    new RoundedRectanglePolygon(new RectangleF(35, parentY, 980, height - parentY - 70), 15));

                var totalScoreText = $"Total Score: {data.TotalScore}";
                var totalScoreOptions = new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(55, parentY + 25),
                    VerticalAlignment = VerticalAlignment.Top
                };
                var totalScoreBounds = TextMeasurer.MeasureBounds(totalScoreText, totalScoreOptions);
                canvas.DrawText(totalScoreOptions, totalScoreText, Brushes.Solid(Color.White), null);
                DrawRankIcon(canvas, new Point(15 + (int)totalScoreBounds.Right, parentY + 17), data.RankPercent);

                var totalStarText = $"x{data.TotalStar}";
                var totalStarOptions = new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(990, parentY + 40),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                var totalStarBounds = TextMeasurer.MeasureBounds(totalStarText, totalStarOptions);
                canvas.DrawImage(m_StarLitImage, m_StarLitImage.Bounds,
                    new RectangleF((int)totalStarBounds.Left - 10 - m_StarLitImage.Width, parentY + 16,
                        m_StarLitImage.Width, m_StarLitImage.Height), KnownResamplers.Bicubic);
                canvas.DrawText(totalStarOptions, totalStarText, Brushes.Solid(Color.White), null);

                for (var i = 0; i < data.List.Count; i++)
                {
                    var floor = data.List[i];
                    var yOffset = parentY + 80 + i * 270;
                    DrawFloorImage(canvas, new Point(50, yOffset), 950, 260, floor, avatarImages, bossImages[floor.Boss[0].Name],
                        buffImages[floor.Buff[0].Name],
                        floor.Buddy == null ? null : buddyImages[floor.Buddy.Id],
                        hard: false);
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

    private void DrawFloorImage(
        DrawingCanvas canvas,
        Point position,
        int width,
        int height,
        AssaultFloorDetail floor,
        Dictionary<int, ZzzAvatar> avatarLookup,
        Image bossImage,
        Image buffImage,
        Image? buddyImage,
        bool hard,
        int? rankPercent = null
    )
    {
        var modulePath = new RoundedRectanglePolygon(new RectangleF(position.X, position.Y, width, height), 15);
        canvas.Fill(CreateGradient(position.X, width,
                hard ? HardGradientStart : NormalGradientStart,
                hard ? HardGradientEnd : NormalGradientEnd),
            modulePath);

        if (!hard)
            canvas.Fill(Brushes.Solid(OverlayColor), modulePath);

        using var region = canvas.CreateRegion(new Rectangle(position, new Size(width, height)));

        var scoreText = floor.Score.ToString();
        var scoreBounds = TextMeasurer.MeasureBounds(scoreText, new TextOptions(Fonts.Normal));
        var rightEdge = width - 25;
        var starX = rightEdge - (int)scoreBounds.Width - (hard ? 0 : 60);
        var rankIconX = starX - 70 - 15 - 123;

        region.DrawText(new RichTextOptions(floor.Boss[0].Name.Length > 25 ? Fonts.Small! : Fonts.Normal)
        {
            Origin = new Vector2(hard ? 215 : 200, hard ? 90 : 30),
            VerticalAlignment = VerticalAlignment.Center,
            WrappingLength = rankPercent.HasValue ? rankIconX - 210 : 500
        }, floor.Boss[0].Name, Brushes.Solid(Color.White), null);
        if (hard)
        {
            var scoreOptions = new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(20, 20),
                VerticalAlignment = VerticalAlignment.Top
            };
            var hardScoreBounds = TextMeasurer.MeasureBounds($"Score: {floor.Score}", scoreOptions);
            region.DrawText(scoreOptions, $"Score: {floor.Score}", Brushes.Solid(Color.White), null);
            DrawRankIcon(region, new Point((int)hardScoreBounds.Right + 15, 12), rankPercent!.Value);

            const int hardStarHeight = 45;
            const int hardStarGap = 4;
            var hardStarX = 945 - (hardStarHeight * 3 + hardStarGap * 2);
            for (var i = 0; i < 3; i++)
            {
                var starImage = i < floor.Star ? m_HardStarLitSmall : m_HardStarUnlitSmall;
                region.DrawImage(starImage, starImage.Bounds,
                    new RectangleF(hardStarX + i * (hardStarHeight + hardStarGap), 15,
                        hardStarHeight, hardStarHeight), KnownResamplers.Bicubic);
            }
        }
        else
        {
            region.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(rightEdge, 15),
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Right
            }, scoreText, Brushes.Solid(Color.White), null);
            for (var i = 2; i >= 0; i--)
            {
                var starImage = i < floor.Star ? m_StarLitSmall : m_StarUnlitSmall;
                region.DrawImage(starImage, starImage.Bounds,
                    new RectangleF(starX - i * 35, 10, starImage.Width, starImage.Height),
                    KnownResamplers.Bicubic);
            }
        }

        if (rankPercent.HasValue && !hard)
            DrawRankIcon(region, new Point(rankIconX, 8), rankPercent.Value);

        region.DrawImage(bossImage, bossImage.Bounds,
            new RectangleF(hard ? 40 : 25, hard ? 75 : 15, bossImage.Width, bossImage.Height),
            KnownResamplers.Bicubic);

        // Roster has only 4 slots; a full avatar squad leaves no room for the buddy slot
        object?[] roster = floor.AvatarList.Count >= 4
            ? [.. floor.AvatarList.Select(x => avatarLookup[x.Id])]
            : [.. floor.AvatarList.Select(x => avatarLookup[x.Id]), buddyImage];

        RosterImageBuilder.Draw(
            roster,
            new RosterLayout(MaxSlots: 4),
            new Point(hard ? 205 : 190, hard ? 120 : 60),
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
            new RectangleF(hard ? 865 : 850, hard ? 160 : 110, buffImage.Width, buffImage.Height),
            KnownResamplers.Bicubic);
    }

    private void DrawRankIcon(DrawingCanvas canvas, Point location, int rankPercent)
    {
        var image = m_RankIcons.First(x => rankPercent <= x.Boundary).Icon;
        _ = canvas.SaveLayer();
        canvas.DrawImage(image, image.Bounds,
            new RectangleF(location.X, location.Y, image.Width, image.Height), KnownResamplers.Bicubic);

        var rankText = $"{(float)rankPercent / 100:N2}%";
        var size = TextMeasurer.MeasureBounds(rankText, new TextOptions(Fonts.Small));

        _ = canvas.Save(RankIconTextDrawingOptions);
        canvas.DrawText(new RichTextOptions(size.Width <= 80 ? Fonts.Small : Fonts.Tiny)
        {
            Origin = new PointF(location.X + 8, location.Y + 12),
        }, rankText, Brushes.Solid(Color.White), null);
        canvas.Restore();
        canvas.Restore();
    }

    private static LinearGradientBrush CreateGradient(int x, int width, Color start, Color end)
    {
        return new LinearGradientBrush(new PointF(x, 0), new PointF(x + width, 0), GradientRepetitionMode.None,
            new ColorStop(0, start), new ColorStop(1, end));
    }

}
