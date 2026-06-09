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

namespace Mehrak.Application.Hsr.EndGame;

internal abstract class HsrEndGameCardServiceBase : CardServiceBase<HsrEndInformation>
{
    protected Image StarLit = null!;
    protected Image StarUnlit = null!;
    protected Image ExtraStar = null!;
    protected Image CycleIcon = null!;

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
            await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hsr.MoCStarName, cancellationToken),
            cancellationToken);
        StarUnlit = StarLit.CloneAs<Rgba32>();
        StarUnlit.Mutate(ctx =>
        {
            ctx.Grayscale();
            ctx.Brightness(0.7f);
        });

        await LoadModeResourcesAsync(cancellationToken);

        CycleIcon = await Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hsr.HourglassName, cancellationToken),
            cancellationToken);

        ExtraStar = await Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hsr.ExtraStarName, cancellationToken),
            cancellationToken);
    }

    protected abstract Task LoadModeResourcesAsync(CancellationToken cancellationToken);

    protected abstract Image GetModeBackgroundImage();

    protected abstract List<(int FloorNumber, HsrEndFloorDetail? Data)> GetFloorDetails(
        HsrEndInformation gameModeData);

    protected abstract string GetStageText(HsrEndInformation gameModeData, HsrEndFloorDetail? floorData, int floorNumber);

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
            .SelectMany(x =>
                (x.Node1?.Avatars ?? []).Concat(x.Node2?.Avatars ?? []).Concat(x.Node3?.Avatars ?? []))
            .DistinctBy(x => x.Id).ToAsyncEnumerable()
            .Select(async (x, token) =>
            {
                await using var stream = await ImageRepository.DownloadFileToStreamAsync(x.ToImageName(), token);
                var avatar = new HsrAvatar(x.Id, x.Level, x.Rarity, x.Rank, await Image.LoadAsync<Rgba32>(stream, token));
                disposables.Add(avatar);
                return avatar;
            })
            .ToDictionaryAsync(x => x.AvatarId, x => x, cancellationToken: cancellationToken);

        var buffImages = await gameModeData.AllFloorDetail
            .SelectMany(x => new[] { x.Node1?.Buff, x.Node2?.Buff, x.Node3?.Buff })
            .OfType<HsrEndBuff>()
            .DistinctBy(x => x.Id)
            .ToAsyncEnumerable()
            .ToDictionaryAsync(
                (x, token) => ValueTask.FromResult(x.Id),
                async (x, token) => await LoadImageFromRepositoryAsync<Rgba32>(x.ToImageName(), disposables, token),
                cancellationToken: cancellationToken);

        var floorDetails = GetFloorDetails(gameModeData);
        var pairBlobHeights = Enumerable.Range(0, 2)
            .Select(pairIndex => Math.Max(
                GetBlobHeight(floorDetails[pairIndex * 2].Data),
                GetBlobHeight(floorDetails[pairIndex * 2 + 1].Data)))
            .ToArray();
        var height = 250 + pairBlobHeights.Sum();

        background.Mutate(ctx =>
        {
            ctx.Resize(new ResizeOptions
            {
                CenterCoordinates = new PointF(ctx.GetCurrentSize().Width / 2f, ctx.GetCurrentSize().Height / 2f),
                Size = new Size(1950, height),
                Mode = ResizeMode.Crop,
                Sampler = KnownResamplers.Bicubic
            });
            var imageSize = ctx.GetCurrentSize();

            ctx.Paint(canvas =>
            {
                DrawHeader(canvas, gameModeData, context);

                var yOffset = 150;
                foreach ((var floorNumber, var floorData) in floorDetails)
                {
                    var xOffset = floorNumber % 2 * 950 + 50;
                    var blobHeight = pairBlobHeights[floorNumber / 2];
                    canvas.DrawRoundedRectangleOverlay(900, blobHeight, new PointF(xOffset, yOffset),
                        new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));

                    var stageText = GetStageText(gameModeData, floorData, floorNumber);

                    var maxTextWidth = floorData?.ExtraStarNum > 0 ? 380 : 450;
                    var bounds = TextMeasurer.MeasureBounds(stageText, new TextOptions(Fonts.Normal));
                    canvas.DrawText(new RichTextOptions(bounds.Width >= maxTextWidth ? Fonts.Small : Fonts.Normal)
                    {
                        Origin = new PointF(xOffset + 20, yOffset + 34),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Center,
                        WrappingLength = maxTextWidth
                    }, stageText, Brushes.Solid(Color.White), null);

                    if (floorData == null || floorData.IsFast)
                    {
                        canvas.DrawText(new RichTextOptions(Fonts.Normal)
                        {
                            Origin = new PointF(xOffset + 450, yOffset + blobHeight / 2),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }, floorData?.IsFast ?? false ? "Quick Clear" : "No Clear Records", Brushes.Solid(Color.White), null);

                        for (var i = 0; i < 3; i++)
                        {
                            var starImage = i < (floorData?.StarNum ?? 0) ? StarLit : StarUnlit;
                            canvas.DrawImage(starImage, starImage.Bounds,
                                new RectangleF(xOffset + 730 + i * 50, yOffset + 5, starImage.Width, starImage.Height),
                                KnownResamplers.Bicubic);
                        }

                        if (floorNumber % 2 == 1)
                        {
                            yOffset += blobHeight + 20;
                        }
                        continue;
                    }

                    var contentEndY = floorData.IsTierce ? 870 : 600;
                    var extraRoom = blobHeight - contentEndY;
                    var separator2Y = extraRoom > 0 && !floorData.IsTierce ? blobHeight / 2 : 335;
                    var separator3Y = 605;
                    var starShift = floorData.ExtraStarNum > 0 ? floorData.ExtraStarNum * 50 : 0;

                    var node1Y = Math.Max(85, (65 + separator2Y) / 2 - 125);
                    var section2End = floorData.IsTierce ? separator3Y : blobHeight;
                    var node2Y = Math.Max(separator2Y + 15, (separator2Y + section2End) / 2 - 125);
                    var node3Y = Math.Max(separator3Y + 15, (separator3Y + blobHeight) / 2 - 125);

                    canvas.Draw(Pens.Solid(Color.White, 2f), new PathBuilder().AddLine(new PointF(xOffset + 20, yOffset + 65),
                        new PointF(xOffset + 880, yOffset + 65)).Build());

                    DrawNodeInformation(canvas, new Point(xOffset + 45, yOffset + node1Y), "Node 1", floorData.Node1,
                        avatarImages, buffImages, DrawNodeExtras);

                    canvas.Draw(Pens.Solid(Color.White, 2f), new PathBuilder().AddLine(new PointF(xOffset + 40, yOffset + separator2Y),
                        new PointF(xOffset + 860, yOffset + separator2Y)).Build());

                    DrawNodeInformation(canvas, new Point(xOffset + 45, yOffset + node2Y), "Node 2", floorData.Node2,
                        avatarImages, buffImages, DrawNodeExtras);

                    if (floorData.IsTierce)
                    {
                        canvas.Draw(Pens.Solid(Color.White, 2f), new PathBuilder().AddLine(new PointF(xOffset + 40, yOffset + separator3Y),
                            new PointF(xOffset + 860, yOffset + separator3Y)).Build());

                        DrawNodeInformation(canvas, new Point(xOffset + 45, yOffset + node3Y), "Node 3", floorData.Node3,
                            avatarImages, buffImages, DrawNodeExtras);
                    }

                    var starX = xOffset + 830;

                    if (floorData.ExtraStarNum > 0)
                    {
                        for (var i = 0; i < floorData.ExtraStarNum; i++)
                        {
                            canvas.DrawImage(ExtraStar, ExtraStar.Bounds,
                                new RectangleF(starX, yOffset + 5, ExtraStar.Width, ExtraStar.Height),
                                KnownResamplers.Bicubic);
                            starX -= 50;
                        }
                    }

                    for (var i = 2; i >= 0; i--)
                    {
                        var starImage = i < floorData.StarNum ? StarLit : StarUnlit;
                        canvas.DrawImage(starImage, starImage.Bounds,
                            new RectangleF(starX, yOffset + 5, starImage.Width, starImage.Height),
                            KnownResamplers.Bicubic);
                        starX -= 50;
                    }

                    canvas.Draw(Pens.Solid(Color.White, 2f), new PathBuilder().AddLine(new PointF(xOffset + 720 - starShift, yOffset + 10),
                        new PointF(xOffset + 720 - starShift, yOffset + 55)).Build());
                    var scoreText = $"Score: {floorData.TotalScore}";
                    canvas.DrawText(new RichTextOptions(Fonts.Normal)
                    {
                        Origin = new PointF(xOffset + 710 - starShift, yOffset + 34),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center
                    }, scoreText, Brushes.Solid(Color.White), null);

                    DrawScoreExtras(canvas, xOffset - starShift, yOffset, scoreText, floorData, gameModeData);

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
        string nodeName,
        HsrEndNodeInformation? nodeData,
        Dictionary<int, HsrAvatar> avatarImages,
        Dictionary<int, Image<Rgba32>> buffImages,
        Action<DrawingCanvas, HsrEndNodeInformation> drawExtras)
    {
        using var region = canvas.CreateRegion(new Rectangle(point, new Size(810, 250)));

        region.DrawText(new RichTextOptions(Fonts.Normal)
        {
            Origin = new PointF(0, 0)
        }, nodeName, Brushes.Solid(Color.White), null);

        if (nodeData == null)
        {
            region.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new PointF(400, 125),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }, "No Clear Record", Brushes.Solid(Color.White), null);
            return;
        }

        region.DrawText(new RichTextOptions(Fonts.Normal)
        {
            Origin = new PointF(810, 0),
            HorizontalAlignment = HorizontalAlignment.Right
        }, $"Score: {nodeData.Score}", Brushes.Solid(Color.White), null);

        drawExtras(region, nodeData);

        RosterImageBuilder.Draw(
            nodeData.Avatars.Select(x => avatarImages[x.Id]),
            new RosterLayout(MaxSlots: 4),
            new Point(10, 45),
            (point, avatar) => avatar.DrawStyledAvatarImage(region, point));

        region.DrawCenteredIcon(buffImages[nodeData.Buff.Id], new PointF(735, 135), 55,
            20f, Color.Black, Color.White);
    }

    private void DrawHeader(DrawingCanvas canvas, HsrEndInformation gameModeData,
        ICardGenerationContext<HsrEndInformation> context)
    {
        var group = gameModeData.Groups[0];
        var modeString = GetModeString();
        canvas.DrawText(new RichTextOptions(Fonts.Title)
        {
            Origin = new Vector2(50, 80),
            VerticalAlignment = VerticalAlignment.Bottom
        }, modeString, Brushes.Solid(Color.White), null);
        var modeTextBounds = TextMeasurer.MeasureBounds(modeString,
            new TextOptions(Fonts.Title) { Origin = new Vector2(50, 80) });
        canvas.DrawText(new RichTextOptions(Fonts.Normal)
        {
            Origin = new Vector2(50, 120),
            VerticalAlignment = VerticalAlignment.Bottom
        },
            $"{group.BeginTime.Day}/{group.BeginTime.Month}/{group.BeginTime.Year} - " +
            $"{group.EndTime.Day}/{group.EndTime.Month}/{group.EndTime.Year}",
            Brushes.Solid(Color.White), null);
        canvas.Draw(Pens.Solid(Color.White, 3f), new PathBuilder().AddLine(new PointF(modeTextBounds.Right + 15, 40),
            new PointF(modeTextBounds.Right + 15, 80)).Build());
        RichTextOptions textOptions = new(Fonts.Title)
        {
            Origin = new Vector2(modeTextBounds.Right + 30, 80),
            VerticalAlignment = VerticalAlignment.Bottom
        };
        var bounds = TextMeasurer.MeasureBounds(gameModeData.StarNum.ToString(), textOptions);
        canvas.DrawText(textOptions, gameModeData.StarNum.ToString(), Brushes.Solid(Color.White), null);
        canvas.DrawImage(StarLit, StarLit.Bounds,
            new RectangleF((int)bounds.Right + 5, 30, StarLit.Width, StarLit.Height),
            KnownResamplers.Bicubic);
        if (gameModeData.ExtraStarNum > 0)
        {
            textOptions = new(textOptions) { Origin = new Vector2(bounds.Right + StarLit.Width + 10, 80) };
            bounds = TextMeasurer.MeasureBounds(gameModeData.ExtraStarNum.ToString(), textOptions);
            canvas.DrawText(textOptions, gameModeData.ExtraStarNum.ToString(), Brushes.Solid(Color.White), null);
            canvas.DrawImage(ExtraStar, ExtraStar.Bounds,
                new RectangleF((int)bounds.Right + 5, 30, ExtraStar.Width, ExtraStar.Height),
                KnownResamplers.Bicubic);
        }

        canvas.DrawText(new RichTextOptions(Fonts.Normal)
        {
            Origin = new Vector2(1900, 80),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        },
            $"{context.GameProfile.Nickname} • TB {context.GameProfile.Level}", Brushes.Solid(Color.White), null);
        canvas.DrawText(new RichTextOptions(Fonts.Normal)
        {
            Origin = new Vector2(1900, 120),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        }, context.GameProfile.GameUid!, Brushes.Solid(Color.White), null);
    }

    protected abstract string GetModeString();

    protected virtual void DrawNodeExtras(DrawingCanvas region, HsrEndNodeInformation nodeData)
    { }

    protected virtual void DrawScoreExtras(DrawingCanvas canvas, int xOffset, int yOffset,
        string scoreText, HsrEndFloorDetail floorData, HsrEndInformation gameModeData)
    { }

    private static int GetBlobHeight(HsrEndFloorDetail? data)
    {
        if (data == null || data.IsFast) return 180;
        if (data.IsTierce && (data.Node1 is not null || data.Node2 is not null || data.Node3 is not null)) return 870;
        return 600;
    }
}
