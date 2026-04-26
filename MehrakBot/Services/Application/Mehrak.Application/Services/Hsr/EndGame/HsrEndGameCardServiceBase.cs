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

namespace Mehrak.Application.Services.Hsr.EndGame;

internal abstract class HsrEndGameCardServiceBase : CardServiceBase<HsrEndInformation>
{
    protected Image StarLit = null!;
    protected Image StarUnlit = null!;
    protected Image CycleIcon = null!;
    protected Image BossCheckmark = null!;

    protected HsrEndGameCardServiceBase(
        string cardTypeName,
        IImageRepository imageRepository,
        ILogger logger,
        IApplicationMetrics metrics)
        : base(cardTypeName, imageRepository, logger, metrics,
            LoadFonts("Assets/Fonts/hsr.ttf", titleSize: 40, normalSize: 28, smallSize: 20))
    {
    }

    public override async Task LoadStaticResourcesAsync(CancellationToken cancellationToken = default)
    {
        StarLit = await Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync("hsr_moc_star", cancellationToken),
            cancellationToken);
        StarUnlit = StarLit.CloneAs<Rgba32>();
        StarUnlit.Mutate(ctx =>
        {
            ctx.Grayscale();
            ctx.Brightness(0.7f);
        });

        await LoadModeResourcesAsync(cancellationToken);

        CycleIcon = await Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync("hsr_hourglass", cancellationToken),
            cancellationToken);

        BossCheckmark = await Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync("hsr_boss_check", cancellationToken),
            cancellationToken);
    }

    protected abstract Task LoadModeResourcesAsync(CancellationToken cancellationToken);

    protected abstract Image GetModeBackgroundImage();

    protected abstract List<(int FloorNumber, HsrEndFloorDetail? Data)> GetFloorDetails(
        HsrEndInformation gameModeData);

    protected abstract string GetStageText(HsrEndInformation gameModeData, int floorNumber);

    protected override Image<Rgba32> CreateBackground()
    {
        return GetModeBackgroundImage().CloneAs<Rgba32>();
    }

    public override async Task RenderCardAsync(
        Image<Rgba32> background,
        ICardGenerationContext<HsrEndInformation> context,
        DisposableBag disposables,
        CancellationToken cancellationToken = default)
    {
        var gameModeData = context.Data;

        var avatarImages = await gameModeData.AllFloorDetail
            .Where(x => x is { Node1: not null, Node2: not null })
            .SelectMany(x => x.Node1!.Avatars.Concat(x.Node2!.Avatars))
            .DistinctBy(x => x.Id).ToAsyncEnumerable()
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

        var buffImages = await gameModeData.AllFloorDetail
            .Where(x => x is { Node1: not null, Node2: not null })
            .SelectMany(x => new HsrEndBuff[] { x.Node1!.Buff, x.Node2!.Buff })
            .Where(x => x is not null)
            .DistinctBy(x => x.Id)
            .ToAsyncEnumerable()
            .ToDictionaryAsync(
                (x, token) => ValueTask.FromResult(x.Id),
                async (x, token) => await LoadImageFromRepositoryAsync<Rgba32>(x.ToImageName(), disposables, token),
                cancellationToken: cancellationToken);

        var lookup = avatarImages.GetAlternateLookup<int>();
        var floorDetails = GetFloorDetails(gameModeData);
        var height = 180 + floorDetails.Chunk(2)
            .Sum(x => x.All(y => y.Data == null || IsSmallBlob(y.Data)) ? 200 : 620);

        background.Mutate(ctx =>
        {
            ctx.Resize(new ResizeOptions
            {
                CenterCoordinates = new PointF(ctx.GetCurrentSize().Width / 2f, ctx.GetCurrentSize().Height / 2f),
                Size = new Size(1950, height),
                Mode = ResizeMode.Crop,
                Sampler = KnownResamplers.Bicubic
            });

            DrawHeader(ctx, gameModeData, context);

            var yOffset = 150;
            foreach ((var floorNumber, var floorData) in floorDetails)
            {
                var xOffset = floorNumber % 2 * 950 + 50;

                if (floorData == null || floorData.IsFast)
                {
                    if ((floorNumber % 2 == 0 && floorNumber + 1 < floorDetails.Count &&
                         !IsSmallBlob(floorDetails[floorNumber + 1].Data)) ||
                        (floorNumber % 2 == 1 && floorNumber - 1 >= 0 &&
                         !IsSmallBlob(floorDetails[floorNumber - 1].Data)))
                    {
                        ctx.DrawRoundedRectangleOverlay(900, 600, new PointF(xOffset, yOffset),
                            new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));
                        ctx.DrawText(new RichTextOptions(Fonts.Normal)
                        {
                            Origin = new PointF(xOffset + 450, yOffset + 280),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }, floorData?.IsFast ?? false ? "Quick Clear" : "No Clear Records", Color.White);
                    }
                    else
                    {
                        ctx.DrawRoundedRectangleOverlay(900, 180, new PointF(xOffset, yOffset),
                            new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));
                        ctx.DrawText(new RichTextOptions(Fonts.Normal)
                        {
                            Origin = new PointF(xOffset + 450, yOffset + 110),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }, floorData?.IsFast ?? false ? "Quick Clear" : "No Clear Records", Color.White);
                    }

                    var stageText = GetStageText(gameModeData, floorNumber);

                    ctx.DrawText(new RichTextOptions(Fonts.Normal)
                    {
                        Origin = new PointF(xOffset + 20, yOffset + 20),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top
                    },
                        floorData?.Name ?? stageText,
                        Color.White);

                    for (var i = 0; i < 3; i++)
                        ctx.DrawImage(i < (floorData?.StarNum ?? 0) ? StarLit : StarUnlit,
                            new Point(xOffset + 730 + i * 50, yOffset + 5), 1f);

                    if (floorNumber % 2 == 1)
                    {
                        var leftIsFull = floorNumber - 1 >= 0 && !IsSmallBlob(floorDetails[floorNumber - 1].Data);
                        yOffset += leftIsFull ? 620 : 200;
                    }
                    continue;
                }

                ctx.DrawRoundedRectangleOverlay(900, 600, new PointF(xOffset, yOffset),
                    new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));
                ctx.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new PointF(xOffset + 20, yOffset + 20),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                }, floorData.Name, Color.White);

                using var node1 = RosterImageBuilder.Build(
                    floorData.Node1!.Avatars.Select(x => lookup[x.Id]),
                    new RosterLayout(MaxSlots: 4));
                using var node2 = RosterImageBuilder.Build(
                    floorData.Node2!.Avatars.Select(x => lookup[x.Id]),
                    new RosterLayout(MaxSlots: 4));
                disposables.Add(node1);
                disposables.Add(node2);
                ctx.DrawLine(Color.White, 2f, new PointF(xOffset + 20, yOffset + 65),
                    new PointF(xOffset + 880, yOffset + 65));
                ctx.DrawText("Node 1", Fonts.Normal, Color.White, new PointF(xOffset + 45, yOffset + 85));
                ctx.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new PointF(xOffset + 855, yOffset + 85),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, $"Score: {floorData.Node1.Score}", Color.White);

                DrawNode1Extras(ctx, xOffset, yOffset, floorData);

                ctx.DrawImage(node1, new Point(xOffset + 55, yOffset + 130), 1f);
                ctx.DrawCenteredIcon(buffImages[floorData.Node1.Buff.Id], new Point(xOffset + 780, yOffset + 220), 55,
                    20f, Color.Black, Color.White);
                ctx.DrawLine(Color.White, 2f, new PointF(xOffset + 40, yOffset + 335),
                    new PointF(xOffset + 860, yOffset + 335));
                ctx.DrawText("Node 2", Fonts.Normal, Color.White, new PointF(xOffset + 45, yOffset + 350));
                ctx.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new PointF(xOffset + 855, yOffset + 350),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, $"Score: {floorData.Node2.Score}", Color.White);

                DrawNode2Extras(ctx, xOffset, yOffset, floorData);

                ctx.DrawImage(node2, new Point(xOffset + 55, yOffset + 395), 1f);
                ctx.DrawCenteredIcon(buffImages[floorData.Node2.Buff.Id], new Point(xOffset + 780, yOffset + 485), 55,
                    20f, Color.Black, Color.White);
                for (var i = 0; i < 3; i++)
                    ctx.DrawImage(i < floorData.StarNum ? StarLit : StarUnlit,
                        new Point(xOffset + 730 + i * 50, yOffset + 5), 1f);
                ctx.DrawLine(Color.White, 2f, new PointF(xOffset + 720, yOffset + 10),
                    new PointF(xOffset + 720, yOffset + 55));
                var scoreText = $"Score: {int.Parse(floorData.Node1.Score) + int.Parse(floorData.Node2.Score)}";
                ctx.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new PointF(xOffset + 710, yOffset + 20),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top
                }, scoreText,
                    Color.White);

                DrawScoreExtras(ctx, xOffset, yOffset, scoreText, floorData, gameModeData);

                if (floorNumber % 2 == 1) yOffset += 620;
            }
        });
    }

    private void DrawHeader(IImageProcessingContext ctx, HsrEndInformation gameModeData,
        ICardGenerationContext<HsrEndInformation> context)
    {
        var group = gameModeData.Groups[0];
        var modeString = GetModeString();
        ctx.DrawText(new RichTextOptions(Fonts.Title)
        {
            Origin = new Vector2(50, 80),
            VerticalAlignment = VerticalAlignment.Bottom
        }, modeString, Color.White);
        var modeTextBounds = TextMeasurer.MeasureBounds(modeString,
            new TextOptions(Fonts.Title) { Origin = new Vector2(50, 80) });
        ctx.DrawText(new RichTextOptions(Fonts.Normal)
        {
            Origin = new Vector2(50, 120),
            VerticalAlignment = VerticalAlignment.Bottom
        },
            $"{group.BeginTime.Day}/{group.BeginTime.Month}/{group.BeginTime.Year} - " +
            $"{group.EndTime.Day}/{group.EndTime.Month}/{group.EndTime.Year}",
            Color.White);
        ctx.DrawLine(Color.White, 3f, new PointF(modeTextBounds.Right + 15, 40),
            new PointF(modeTextBounds.Right + 15, 80));
        RichTextOptions textOptions = new(Fonts.Title)
        {
            Origin = new Vector2(modeTextBounds.Right + 30, 80),
            VerticalAlignment = VerticalAlignment.Bottom
        };
        var bounds = TextMeasurer.MeasureBounds(gameModeData.StarNum.ToString(), textOptions);
        ctx.DrawText(textOptions, gameModeData.StarNum.ToString(), Color.White);
        ctx.DrawImage(StarLit, new Point((int)bounds.Right + 5, 30), 1f);

        ctx.DrawText(new RichTextOptions(Fonts.Normal)
        {
            Origin = new Vector2(1900, 80),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        },
            $"{context.GameProfile.Nickname} • TB {context.GameProfile.Level}", Color.White);
        ctx.DrawText(new RichTextOptions(Fonts.Normal)
        {
            Origin = new Vector2(1900, 120),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        }, context.GameProfile.GameUid!, Color.White);
    }

    protected abstract string GetModeString();

    protected virtual void DrawNode1Extras(IImageProcessingContext ctx, int xOffset, int yOffset,
        HsrEndFloorDetail floorData)
    { }

    protected virtual void DrawNode2Extras(IImageProcessingContext ctx, int xOffset, int yOffset,
        HsrEndFloorDetail floorData)
    { }

    protected virtual void DrawScoreExtras(IImageProcessingContext ctx, int xOffset, int yOffset,
        string scoreText, HsrEndFloorDetail floorData, HsrEndInformation gameModeData)
    { }

    private static bool IsSmallBlob(HsrEndFloorDetail? data)
    {
        return data == null || data.IsFast;
    }
}
