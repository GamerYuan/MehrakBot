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
        m_StarLit = await Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hsr.MoCStarName, cancellationToken),
            cancellationToken);
        m_StarUnlit = m_StarLit.CloneAs<Rgba32>();
        m_StarUnlit.Mutate(ctx =>
        {
            ctx.Grayscale();
            ctx.Brightness(0.7f);
        });
        m_CycleIcon = await Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hsr.HourglassName, cancellationToken),
            cancellationToken);

        StaticBackground = await Image.LoadAsync<Rgba32>(
            await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hsr.MoCBackgroundName, cancellationToken),
            cancellationToken);
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

        var avatarImages = await memoryData.AllFloorDetail!
            .SelectMany(x => x.Node1.Avatars.Concat(x.Node2.Avatars))
            .DistinctBy(x => x.Id)
            .ToAsyncEnumerable()
            .Select(async (x, token) =>
            {
                await using var stream = await ImageRepository.DownloadFileToStreamAsync(x.ToImageName(), token);
                var avatar = new HsrAvatar(x.Id, x.Level, x.Rarity, x.Rank, await Image.LoadAsync<Rgba32>(stream, token));
                disposables.Add(avatar);
                return avatar;
            })
            .ToDictionaryAsync(x => x.AvatarId, x => x, cancellationToken: cancellationToken);

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
        var height = 210 + floorDetails.Chunk(2)
            .Sum(x => x.All(y => y.Data == null || IsSmallBlob(y.Data)) ? 200 : 520);

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

                    if (floorData == null || floorData.IsFast)
                    {
                        if ((floorNumber % 2 == 0 && floorNumber + 1 < floorDetails.Count &&
                             !IsSmallBlob(floorDetails[floorNumber + 1].Data)) ||
                            (floorNumber % 2 == 1 && floorNumber - 1 >= 0 &&
                             !IsSmallBlob(floorDetails[floorNumber - 1].Data)))
                        {
                            canvas.DrawRoundedRectangleOverlay(700, 500, new PointF(xOffset, yOffset),
                                new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));
                            canvas.DrawText(new RichTextOptions(Fonts.Normal)
                            {
                                Origin = new PointF(xOffset + 350, yOffset + 280),
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center
                            }, floorData?.IsFast ?? false ? "Quick Clear" : "No Clear Records", Brushes.Solid(Color.White), null);
                        }
                        else
                        {
                            canvas.DrawRoundedRectangleOverlay(700, 180, new PointF(xOffset, yOffset),
                                new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));
                            canvas.DrawText(new RichTextOptions(Fonts.Normal)
                            {
                                Origin = new PointF(xOffset + 350, yOffset + 110),
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center
                            }, floorData?.IsFast ?? false ? "Quick Clear" : "No Clear Records", Brushes.Solid(Color.White), null);
                        }

                        var stageText =
                            $"{memoryData.Groups[0].Name} ({HsrUtility.GetRomanNumeral(floorNumber + 1)})";
                        canvas.DrawText(new RichTextOptions(Fonts.Normal)
                        {
                            Origin = new PointF(xOffset + 20, yOffset + 20),
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Top
                        }, floorData?.Name ?? stageText, Brushes.Solid(Color.White), null);

                        for (var i = 0; i < 3; i++)
                        {
                            var starImage = i < (floorData?.StarNum ?? 0) ? m_StarLit : m_StarUnlit;
                            canvas.DrawImage(starImage, starImage.Bounds,
                                new RectangleF(xOffset + 530 + i * 50, yOffset + 5, starImage.Width, starImage.Height),
                                KnownResamplers.Bicubic);
                        }

                        if (floorNumber % 2 == 1)
                        {
                            var leftIsFull = floorNumber - 1 >= 0 && !IsSmallBlob(floorDetails[floorNumber - 1].Data);
                            yOffset += leftIsFull ? 520 : 200;
                        }

                        continue;
                    }

                    canvas.DrawRoundedRectangleOverlay(700, 500, new PointF(xOffset, yOffset),
                        new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));
                    canvas.DrawText(new RichTextOptions(Fonts.Normal)
                    {
                        Origin = new PointF(xOffset + 20, yOffset + 20),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top
                    }, floorData.Name, Brushes.Solid(Color.White), null);

                    RosterImageBuilder.Draw(
                        floorData.Node1.Avatars.Select(x => avatarImages[x.Id]),
                        new RosterLayout(MaxSlots: 4),
                        new Point(xOffset + 25, yOffset + 65),
                        (point, avatar) => avatar.DrawStyledAvatarImage(canvas, point));
                    canvas.Draw(Pens.Solid(Color.White, 2f), new PathBuilder().AddLine(new PointF(xOffset + 20, yOffset + 270),
                        new PointF(xOffset + 680, yOffset + 270)).Build());
                    RosterImageBuilder.Draw(
                        floorData.Node2.Avatars.Select(x => avatarImages[x.Id]),
                        new RosterLayout(MaxSlots: 4),
                        new Point(xOffset + 25, yOffset + 295),
                        (point, avatar) => avatar.DrawStyledAvatarImage(canvas, point));

                    for (var i = 0; i < 3; i++)
                    {
                        var starImage = i < floorData.StarNum ? m_StarLit : m_StarUnlit;
                        canvas.DrawImage(starImage, starImage.Bounds,
                            new RectangleF(xOffset + 530 + i * 50, yOffset + 5, starImage.Width, starImage.Height),
                            KnownResamplers.Bicubic);
                    }
                    canvas.Draw(Pens.Solid(Color.White, 2f), new PathBuilder().AddLine(new PointF(xOffset + 520, yOffset + 10),
                        new PointF(xOffset + 520, yOffset + 55)).Build());
                    canvas.DrawText(new RichTextOptions(Fonts.Normal)
                    {
                        Origin = new PointF(xOffset + 470, yOffset + 20),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Top
                    }, floorData.RoundNum.ToString(), Brushes.Solid(Color.White), null);
                    canvas.DrawImage(m_CycleIcon, m_CycleIcon.Bounds,
                        new RectangleF(xOffset + 470, yOffset + 10, m_CycleIcon.Width, m_CycleIcon.Height),
                        KnownResamplers.Bicubic);
                    if (floorNumber % 2 == 1) yOffset += 520;
                }

                canvas.DrawAttribution(
                    new AttributionStyle(TextColor: Color.White, ShadowStyle:
                        new DropShadowTextStyle(ShadowOffsetX: 2, ShadowOffsetY: 2,
                            ShadowColor: Color.FromPixel(new Rgba32(0, 0, 0, 0.75f)))),
                    new RichTextOptions(Fonts.Tiny)
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

    private static bool IsSmallBlob(FloorDetail? floor)
    {
        return floor == null || floor.IsFast;
    }
}
