#region

using System.Numerics;
using Mehrak.Application.Models;
using Mehrak.Application.Renderers;
using Mehrak.Application.Renderers.Extensions;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.GameApi.Hsr.Types;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Services.Hsr.Memory;

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
            await ImageRepository.DownloadFileToStreamAsync("hsr_moc_star", cancellationToken),
            cancellationToken);
        m_StarUnlit = m_StarLit.CloneAs<Rgba32>();
        m_StarUnlit.Mutate(ctx =>
        {
            ctx.Grayscale();
            ctx.Brightness(0.7f);
        });
        m_CycleIcon = await Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync("hsr_hourglass", cancellationToken),
            cancellationToken);

        StaticBackground = await Image.LoadAsync<Rgba32>(
            await ImageRepository.DownloadFileToStreamAsync("hsr_moc_bg", cancellationToken),
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
            .ToDictionaryAsync(x => x, x =>
            {
                var styledImage = x.GetStyledAvatarImage();
                disposables.Add(styledImage);
                return styledImage;
            }, HsrAvatarIdComparer.Instance, cancellationToken: cancellationToken);

        var lookup = avatarImages.GetAlternateLookup<int>();
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
        var height = 180 + floorDetails.Chunk(2)
            .Sum(x => x.All(y => y.Data == null || IsSmallBlob(y.Data)) ? 200 : 520);

        background.Mutate(ctx =>
        {
            ctx.Resize(0, height, KnownResamplers.Bicubic);
            ctx.Crop(new Rectangle((ctx.GetCurrentSize().Width - 1550) / 2, 0, 1550, height));

            ctx.DrawText(new RichTextOptions(Fonts.Title)
            {
                Origin = new Vector2(50, 80),
                VerticalAlignment = VerticalAlignment.Bottom
            }, "Memory of Chaos", Color.White);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(50, 110),
                VerticalAlignment = VerticalAlignment.Bottom
            },
                $"{memoryData.StartTime.Day}/{memoryData.StartTime.Month}/{memoryData.StartTime.Year} - " +
                $"{memoryData.EndTime.Day}/{memoryData.EndTime.Month}/{memoryData.EndTime.Year}",
                Color.White);
            ctx.DrawLine(Color.White, 3f, new PointF(415, 40), new PointF(415, 80));
            RichTextOptions textOptions = new(Fonts.Title)
            {
                Origin = new Vector2(435, 80),
                VerticalAlignment = VerticalAlignment.Bottom
            };
            var bounds = TextMeasurer.MeasureBounds(memoryData.StarNum.ToString(), textOptions);
            ctx.DrawText(textOptions, memoryData.StarNum.ToString(), Color.White);
            ctx.DrawImage(m_StarLit, new Point((int)bounds.Right + 5, 30), 1f);

            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(1500, 80),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom
            },
                $"{context.GameProfile.Nickname} • TB {context.GameProfile.Level}", Color.White);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(1500, 110),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom
            }, context.GameProfile.GameUid!, Color.White);

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
                        ctx.DrawRoundedRectangleOverlay(700, 500, new PointF(xOffset, yOffset),
                            new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));
                        ctx.DrawText(new RichTextOptions(Fonts.Normal)
                        {
                            Origin = new PointF(xOffset + 350, yOffset + 280),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }, floorData?.IsFast ?? false ? "Quick Clear" : "No Clear Records", Color.White);
                    }
                    else
                    {
                        ctx.DrawRoundedRectangleOverlay(700, 180, new PointF(xOffset, yOffset),
                            new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));
                        ctx.DrawText(new RichTextOptions(Fonts.Normal)
                        {
                            Origin = new PointF(xOffset + 350, yOffset + 110),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }, floorData?.IsFast ?? false ? "Quick Clear" : "No Clear Records", Color.White);
                    }

                    var stageText =
                        $"{memoryData.Groups[0].Name} ({HsrUtility.GetRomanNumeral(floorNumber + 1)})";
                    ctx.DrawText(new RichTextOptions(Fonts.Normal)
                    {
                        Origin = new PointF(xOffset + 20, yOffset + 20),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top
                    }, floorData?.Name ?? stageText, Color.White);

                    for (var i = 0; i < 3; i++)
                        ctx.DrawImage(i < (floorData?.StarNum ?? 0) ? m_StarLit : m_StarUnlit,
                            new Point(xOffset + 530 + i * 50, yOffset + 5), 1f);

                    if (floorNumber % 2 == 1)
                    {
                        var leftIsFull = floorNumber - 1 >= 0 && !IsSmallBlob(floorDetails[floorNumber - 1].Data);
                        yOffset += leftIsFull ? 520 : 200;
                    }

                    continue;
                }

                ctx.DrawRoundedRectangleOverlay(700, 500, new PointF(xOffset, yOffset),
                    new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));
                ctx.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new PointF(xOffset + 20, yOffset + 20),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                }, floorData.Name, Color.White);

                var node1 = RosterImageBuilder.Build(
                    floorData.Node1.Avatars.Select(x => lookup[x.Id]),
                    new RosterLayout(MaxSlots: 4));
                var node2 = RosterImageBuilder.Build(
                    floorData.Node2.Avatars.Select(x => lookup[x.Id]),
                    new RosterLayout(MaxSlots: 4));
                disposables.Add(node1);
                disposables.Add(node2);
                ctx.DrawImage(node1, new Point(xOffset + 25, yOffset + 65), 1f);
                ctx.DrawLine(Color.White, 2f, new PointF(xOffset + 20, yOffset + 270),
                    new PointF(xOffset + 680, yOffset + 270));
                ctx.DrawImage(node2, new Point(xOffset + 25, yOffset + 295), 1f);
                for (var i = 0; i < 3; i++)
                    ctx.DrawImage(i < floorData.StarNum ? m_StarLit : m_StarUnlit,
                        new Point(xOffset + 530 + i * 50, yOffset + 5), 1f);
                ctx.DrawLine(Color.White, 2f, new PointF(xOffset + 520, yOffset + 10),
                    new PointF(xOffset + 520, yOffset + 55));
                ctx.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new PointF(xOffset + 470, yOffset + 20),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top
                }, floorData.RoundNum.ToString(), Color.White);
                ctx.DrawImage(m_CycleIcon, new Point(xOffset + 470, yOffset + 10), 1f);
                if (floorNumber % 2 == 1) yOffset += 520;
            }
        });
    }

    private static bool IsSmallBlob(FloorDetail? floor)
    {
        return floor == null || floor.IsFast;
    }
}
