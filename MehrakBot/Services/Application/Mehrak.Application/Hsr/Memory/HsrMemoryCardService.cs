#region

using System.Numerics;
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

namespace Mehrak.Application.Hsr.Memory;

internal class HsrMemoryCardService : CardServiceBase<HsrMemoryInformation>
{
    private Image m_StarLit = null!;
    private Image m_StarUnlit = null!;
    private Image m_CycleIcon = null!;
    private Image m_ExtraStar = null!;

    public HsrMemoryCardService(IImageRepository imageRepository,
        ILogger<HsrMemoryCardService> logger,
        IApplicationMetrics metrics)
        : base(
            "Hsr MoC",
            imageRepository,
            logger,
            metrics,
            LoadFonts("Assets/Fonts/hsr.ttf", 40f, 28f, smallSize: 20f))
    {
    }

    public override async Task LoadStaticResourcesAsync(CancellationToken cancellationToken = default)
    {
        var downloadTasks = new[]
        {
            ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hsr.MoCStarName, cancellationToken),
            ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hsr.HourglassName, cancellationToken),
            ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hsr.MoCBackgroundName, cancellationToken),
            ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hsr.ExtraStarName, cancellationToken)
        };
        await Task.WhenAll(downloadTasks);

        var (starStream, cycleStream, bgStream, extraStarStream) =
            (downloadTasks[0].Result, downloadTasks[1].Result, downloadTasks[2].Result, downloadTasks[3].Result);

        m_StarLit = await Image.LoadAsync(starStream, cancellationToken);
        m_StarUnlit = m_StarLit.CloneAs<Rgba32>();
        m_StarUnlit.Mutate(ctx =>
        {
            ctx.Grayscale();
            ctx.Brightness(0.7f);
        });
        m_CycleIcon = await Image.LoadAsync(cycleStream, cancellationToken);
        m_ExtraStar = await Image.LoadAsync(extraStarStream, cancellationToken);

        StaticBackground = await Image.LoadAsync<Rgba32>(bgStream, cancellationToken);
        StaticBackground.Mutate(ctx =>
        {
            ctx.Brightness(0.5f);
            ctx.GaussianBlur(5);
        });
    }

    protected override Image<Rgba32> CreateBackground()
    {
        return StaticBackground!.CloneAs<Rgba32>();
    }

    public override async Task RenderCardAsync(
        Image<Rgba32> background,
        ICardGenerationContext<HsrMemoryInformation> context,
        DisposableBag disposables,
        CancellationToken cancellationToken = default)
    {
        var memoryData = context.Data;

        var avatarData = memoryData.AllFloorDetail!
            .SelectMany(x =>
                (x.Node1?.Avatars ?? []).Concat(x.Node2?.Avatars ?? []).Concat(x.Node3?.Avatars ?? []))
            .DistinctBy(x => x.Id)
            .ToList();

        var avatarTasks = avatarData
            .Select(async x =>
            {
                await using var stream = await ImageRepository.DownloadFileToStreamAsync(x.ToImageName(), cancellationToken);
                var avatar = new HsrAvatar(x.Id, x.Level, x.Rarity, x.Rank, await Image.LoadAsync<Rgba32>(stream, cancellationToken));
                disposables.Add(avatar);
                return avatar;
            })
            .ToList();
        await Task.WhenAll(avatarTasks);

        var avatarImages = avatarTasks.ToDictionary(x => x.Result.AvatarId, x => x.Result);

        List<(int FloorNumber, FloorDetail? Data)> floorDetails =
        [
            .. Enumerable.Range(0, 12)
                .Select(floorIndex =>
                {
                    var floorData = memoryData.AllFloorDetail!
                        .FirstOrDefault(x => HsrUtility.GetFloorNumber(x.Name) - 1 == floorIndex);
                    return (FloorNumber: floorIndex, Data: floorData);
                })
        ];

        var pairBlobHeights = Enumerable.Range(0, 6)
            .Select(pairIndex => Math.Max(
                GetBlobHeight(floorDetails[pairIndex * 2].Data),
                GetBlobHeight(floorDetails[pairIndex * 2 + 1].Data)))
            .ToArray();
        var height = 210 + (20 * 6) + pairBlobHeights.Sum();

        background.Mutate(ctx =>
        {
            ctx.Resize(new ResizeOptions
            {
                CenterCoordinates = new PointF(ctx.GetCurrentSize().Width / 2f, ctx.GetCurrentSize().Height / 2f),
                Size = new Size(1550, height),
                Mode = ResizeMode.Crop,
                Sampler = KnownResamplers.Bicubic
            });
            var imageSize = ctx.GetCurrentSize();

            ctx.Paint(canvas =>
            {
                canvas.DrawText(new RichTextOptions(Fonts.Title)
                {
                    Origin = new Vector2(50, 80),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, "Memory of Chaos", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(50, 110),
                    VerticalAlignment = VerticalAlignment.Bottom
                },
                    $"{memoryData.StartTime.Day}/{memoryData.StartTime.Month}/{memoryData.StartTime.Year} - " +
                    $"{memoryData.EndTime.Day}/{memoryData.EndTime.Month}/{memoryData.EndTime.Year}",
                    Brushes.Solid(Color.White), null);
                canvas.Draw(Pens.Solid(Color.White, 3f), new PathBuilder().AddLine(new PointF(415, 40), new PointF(415, 80)).Build());
                RichTextOptions textOptions = new(Fonts.Title)
                {
                    Origin = new Vector2(435, 80),
                    VerticalAlignment = VerticalAlignment.Bottom
                };
                var bounds = TextMeasurer.MeasureBounds(memoryData.StarNum.ToString(), textOptions);
                canvas.DrawText(textOptions, memoryData.StarNum.ToString(), Brushes.Solid(Color.White), null);
                canvas.DrawImage(m_StarLit, m_StarLit.Bounds,
                    new RectangleF((int)bounds.Right + 5, 30, m_StarLit.Width, m_StarLit.Height),
                    KnownResamplers.Bicubic);

                if (memoryData.ExtraStarNum > 0)
                {
                    textOptions.Origin = new Vector2((int)bounds.Right + 5 + m_StarLit.Width + 10, 80);
                    bounds = TextMeasurer.MeasureBounds($"{memoryData.ExtraStarNum}", textOptions);
                    canvas.DrawText(textOptions, $"{memoryData.ExtraStarNum}", Brushes.Solid(Color.White), null);
                    canvas.DrawImage(m_ExtraStar, m_ExtraStar.Bounds,
                        new RectangleF((int)bounds.Right + 5, 30, m_ExtraStar.Width, m_ExtraStar.Height),
                        KnownResamplers.Bicubic);
                }

                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(1500, 80),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom
                },
                    $"{context.GameProfile.Nickname} • TB {context.GameProfile.Level}", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(1500, 110),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom
                }, context.GameProfile.GameUid!, Brushes.Solid(Color.White), null);

                var yOffset = 150;
                foreach ((var floorNumber, var floorData) in floorDetails)
                {
                    var xOffset = floorNumber % 2 * 750 + 50;
                    var blobHeight = pairBlobHeights[floorNumber / 2];

                    canvas.DrawRoundedRectangleOverlay(700, blobHeight, new PointF(xOffset, yOffset),
                        new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));

                    var stageText =
                        $"{memoryData.Groups[0].Name} ({HsrUtility.GetRomanNumeral(floorNumber + 1)}){(floorData?.IsTierce == true ? " Starward Mode" : "")}";

                    var extraStarShift = (floorData?.ExtraStarNum ?? 0) * 50;
                    var roundNumberText = floorData?.RoundNum.ToString() ?? "";
                    var roundNumberWidth = (int)TextMeasurer.MeasureBounds(roundNumberText, new TextOptions(Fonts.Normal)).Width;
                    var maxTextWidth = 680 - 35 - roundNumberWidth - m_CycleIcon.Width - 150 - extraStarShift;

                    var stageTextBounds = TextMeasurer.MeasureBounds(stageText, new TextOptions(Fonts.Normal));
                    canvas.DrawText(new RichTextOptions(stageTextBounds.Width >= maxTextWidth ? Fonts.Small : Fonts.Normal)
                    {
                        Origin = new PointF(xOffset + 20, yOffset + 25),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Center,
                        WrappingLength = maxTextWidth
                    }, stageText, Brushes.Solid(Color.White), null);

                    var starX = xOffset + 630;

                    if (floorData?.ExtraStarNum > 0)
                    {
                        for (var i = 0; i < floorData.ExtraStarNum; i++)
                        {
                            canvas.DrawImage(m_ExtraStar, m_ExtraStar.Bounds,
                                new RectangleF(starX, yOffset + 5, m_ExtraStar.Width, m_ExtraStar.Height),
                                KnownResamplers.Bicubic);
                            starX -= 50;
                        }
                    }

                    for (var i = 2; i >= 0; i--)
                    {
                        var starImage = i < (floorData?.StarNum ?? 0) ? m_StarLit : m_StarUnlit;
                        canvas.DrawImage(starImage, starImage.Bounds,
                            new RectangleF(starX, yOffset + 5, starImage.Width, starImage.Height),
                            KnownResamplers.Bicubic);
                        starX -= 50;
                    }

                    if (floorData == null || floorData.IsFast)
                    {
                        canvas.DrawText(new RichTextOptions(Fonts.Normal)
                        {
                            Origin = new PointF(xOffset + 350, yOffset + blobHeight / 2 + 10),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }, floorData?.IsFast ?? false ? "Quick Clear" : "No Clear Records",
                            Brushes.Solid(Color.White), null);
                    }
                    else
                    {
                        const int contentStart = 65;
                        const int regionHeight = 200;
                        var sectionHeight = (blobHeight - contentStart) / (floorData.IsTierce ? 3 : 2);

                        var sep1Y = contentStart + sectionHeight;
                        var sep2Y = contentStart + sectionHeight * 2;

                        var nodeSectionOffset = (sectionHeight - regionHeight) / 2;
                        DrawNodeInformation(canvas, new Point(xOffset + 25, yOffset + contentStart + nodeSectionOffset),
                            floorData.Node1, avatarImages);

                        canvas.Draw(Pens.Solid(Color.White, 2f), new PathBuilder().AddLine(
                            new PointF(xOffset + 20, yOffset + sep1Y),
                            new PointF(xOffset + 680, yOffset + sep1Y)).Build());

                        DrawNodeInformation(canvas, new Point(xOffset + 25, yOffset + sep1Y + nodeSectionOffset),
                            floorData.Node2, avatarImages);

                        if (floorData.IsTierce)
                        {
                            canvas.Draw(Pens.Solid(Color.White, 2f), new PathBuilder().AddLine(
                                new PointF(xOffset + 20, yOffset + sep2Y),
                                new PointF(xOffset + 680, yOffset + sep2Y)).Build());
                            DrawNodeInformation(canvas, new Point(xOffset + 25, yOffset + sep2Y + nodeSectionOffset),
                                floorData.Node3, avatarImages);
                        }

                        canvas.Draw(Pens.Solid(Color.White, 2f), new PathBuilder().AddLine(
                            new PointF(xOffset + 520 - extraStarShift, yOffset + 10),
                            new PointF(xOffset + 520 - extraStarShift, yOffset + 55)).Build());
                        canvas.DrawText(new RichTextOptions(Fonts.Normal)
                        {
                            Origin = new PointF(xOffset + 470 - extraStarShift, yOffset + 25),
                            HorizontalAlignment = HorizontalAlignment.Right,
                            VerticalAlignment = VerticalAlignment.Center
                        }, floorData.RoundNum.ToString(), Brushes.Solid(Color.White), null);
                        canvas.DrawImage(m_CycleIcon, m_CycleIcon.Bounds,
                            new RectangleF(xOffset + 470 - extraStarShift, yOffset + 25 - m_CycleIcon.Height / 2, m_CycleIcon.Width, m_CycleIcon.Height),
                            KnownResamplers.Bicubic);
                    }

                    if (floorNumber % 2 == 1) yOffset += blobHeight + 20;
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

    private void DrawNodeInformation(
        DrawingCanvas canvas,
        Point point,
        NodeInformation? nodeData,
        Dictionary<int, HsrAvatar> avatarImages)
    {
        using var region = canvas.CreateRegion(new Rectangle(point, new Size(650, 200)));

        if (nodeData == null)
        {
            region.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new PointF(325, 100),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }, "No Clear Record", Brushes.Solid(Color.White), null);
            return;
        }

        RosterImageBuilder.Draw(
            nodeData.Avatars.Select(x => avatarImages[x.Id]),
            new RosterLayout(MaxSlots: 4),
            new Point(10, 5),
            (point, avatar) => avatar.DrawStyledAvatarImage(region, point));
    }

    private static int GetBlobHeight(FloorDetail? floor)
    {
        if (floor == null || floor.IsFast) return 180;
        if (floor.IsTierce) return 730;
        return 500;
    }
}
