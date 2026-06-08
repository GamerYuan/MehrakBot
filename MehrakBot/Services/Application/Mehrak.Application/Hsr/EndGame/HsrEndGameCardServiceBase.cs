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
            .ToDictionaryAsync(x => x.AvatarId, x => x, cancellationToken: cancellationToken);

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

        var floorDetails = GetFloorDetails(gameModeData);
        var height = 210 + floorDetails.Chunk(2)
            .Sum(((int FloorNumber, HsrEndFloorDetail? Data)[] x) =>
                x.Max(((int FloorNumber, HsrEndFloorDetail? Data) y) => GetBlobHeight(y.Data)));

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

                    if (floorData == null || floorData.IsFast)
                    {
                        if ((floorNumber % 2 == 0 && floorNumber + 1 < floorDetails.Count &&
                             !IsSmallBlob(floorDetails[floorNumber + 1].Data)) ||
                            (floorNumber % 2 == 1 && floorNumber - 1 >= 0 &&
                             !IsSmallBlob(floorDetails[floorNumber - 1].Data)))
                        {
                            canvas.DrawRoundedRectangleOverlay(900, 600, new PointF(xOffset, yOffset),
                                new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));
                            canvas.DrawText(new RichTextOptions(Fonts.Normal)
                            {
                                Origin = new PointF(xOffset + 450, yOffset + 280),
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center
                            }, floorData?.IsFast ?? false ? "Quick Clear" : "No Clear Records", Brushes.Solid(Color.White), null);
                        }
                        else
                        {
                            canvas.DrawRoundedRectangleOverlay(900, 180, new PointF(xOffset, yOffset),
                                new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));
                            canvas.DrawText(new RichTextOptions(Fonts.Normal)
                            {
                                Origin = new PointF(xOffset + 450, yOffset + 110),
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center
                            }, floorData?.IsFast ?? false ? "Quick Clear" : "No Clear Records", Brushes.Solid(Color.White), null);
                        }

                        var stageText = GetStageText(gameModeData, floorNumber);

                        canvas.DrawText(new RichTextOptions(Fonts.Normal)
                        {
                            Origin = new PointF(xOffset + 20, yOffset + 20),
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Top
                        },
                            floorData?.Name ?? stageText,
                            Brushes.Solid(Color.White), null);

                        for (var i = 0; i < 3; i++)
                        {
                            var starImage = i < (floorData?.StarNum ?? 0) ? StarLit : StarUnlit;
                            canvas.DrawImage(starImage, starImage.Bounds,
                                new RectangleF(xOffset + 730 + i * 50, yOffset + 5, starImage.Width, starImage.Height),
                                KnownResamplers.Bicubic);
                        }

                        if (floorNumber % 2 == 1)
                        {
                            yOffset += GetBlobHeight(floorNumber - 1 >= 0 ? floorDetails[floorNumber - 1].Data : null);
                        }
                        continue;
                    }

                    canvas.DrawRoundedRectangleOverlay(900, 600, new PointF(xOffset, yOffset),
                        new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));
                    canvas.DrawText(new RichTextOptions(Fonts.Normal)
                    {
                        Origin = new PointF(xOffset + 20, yOffset + 20),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top
                    }, floorData.Name, Brushes.Solid(Color.White), null);

                    canvas.Draw(Pens.Solid(Color.White, 2f), new PathBuilder().AddLine(new PointF(xOffset + 20, yOffset + 65),
                        new PointF(xOffset + 880, yOffset + 65)).Build());

                    DrawNodeInformation(canvas, new Point(xOffset + 45, yOffset + 85), "Node 1", floorData.Node1, avatarImages, buffImages, DrawNodeExtras);

                    canvas.Draw(Pens.Solid(Color.White, 2f), new PathBuilder().AddLine(new PointF(xOffset + 40, yOffset + 335),
                        new PointF(xOffset + 860, yOffset + 335)).Build());

                    DrawNodeInformation(canvas, new Point(xOffset + 45, yOffset + 350), "Node 2", floorData.Node2, avatarImages, buffImages, DrawNodeExtras);

                    for (var i = 0; i < 3; i++)
                    {
                        var starImage = i < floorData.StarNum ? StarLit : StarUnlit;
                        canvas.DrawImage(starImage, starImage.Bounds,
                            new RectangleF(xOffset + 730 + i * 50, yOffset + 5, starImage.Width, starImage.Height),
                            KnownResamplers.Bicubic);
                    }

                    canvas.Draw(Pens.Solid(Color.White, 2f), new PathBuilder().AddLine(new PointF(xOffset + 720, yOffset + 10),
                        new PointF(xOffset + 720, yOffset + 55)).Build());
                    var scoreText = $"Score: {floorData.TotalScore}";
                    canvas.DrawText(new RichTextOptions(Fonts.Normal)
                    {
                        Origin = new PointF(xOffset + 710, yOffset + 20),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Top
                    }, scoreText,
                        Brushes.Solid(Color.White), null);

                    DrawScoreExtras(canvas, xOffset, yOffset, scoreText, floorData, gameModeData);

                    if (floorNumber % 2 == 1) yOffset += 620;
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
        Action<DrawingCanvas, HsrEndNodeInformation> drawExtras
    )
    {
        using var region = canvas.CreateRegion(new Rectangle(point, new Size(810, 250)));

        if (nodeData == null)
        {
            region.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new PointF(405, 125),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }, "No Clear Record", Brushes.Solid(Color.White), null);
            return;
        }

        region.DrawText(new RichTextOptions(Fonts.Normal)
        {
            Origin = new PointF(0, 0)
        }, nodeName, Brushes.Solid(Color.White), null);
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
        var starLit = StarLit;
        canvas.DrawImage(starLit, starLit.Bounds,
            new RectangleF((int)bounds.Right + 5, 30, starLit.Width, starLit.Height),
            KnownResamplers.Bicubic);

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
        if (data == null || data.IsFast) return 200;
        if (data.IsTierce && (data.Node1 is not null || data.Node2 is not null || data.Node3 is not null)) return 870;
        return 620;
    }

    private static bool IsSmallBlob(HsrEndFloorDetail? data)
    {
        return data == null || data.IsFast;
    }
}
