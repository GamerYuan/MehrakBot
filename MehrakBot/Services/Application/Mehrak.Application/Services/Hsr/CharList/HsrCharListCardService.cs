#region

using System.Numerics;
using Mehrak.Application.Renderers;
using Mehrak.Application.Renderers.Extensions;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Utility;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.GameApi.Hsr.Types;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Services.Hsr.CharList;

internal class HsrCharListCardService : CardServiceBase<IEnumerable<HsrCharacterInformation>>
{
    private static readonly Color GoldBackgroundColor = Color.ParseHex("BC8F60");
    private static readonly Color PurpleBackgroundColor = Color.ParseHex("7651B3");
    private static readonly Color BlueBackgroundColor = Color.FromRgb(90, 131, 187);
    private static readonly Color WhiteBackgroundColor = Color.FromRgb(128, 128, 130);

    private static readonly Color[] RarityColors =
    [
        WhiteBackgroundColor,
        BlueBackgroundColor,
        PurpleBackgroundColor,
        GoldBackgroundColor
    ];

    private static readonly Color PurpleForegroundColor = Color.FromRgb(204, 173, 255);

    private static readonly Color NormalConstColor = Color.FromRgba(69, 69, 69, 200);
    private static readonly Color GoldConstTextColor = Color.ParseHex("8A6500");

    private static readonly string[] Elements =
        ["physical", "fire", "ice", "lightning", "wind", "quantum", "imaginary"];

    private static readonly Dictionary<string, Color> ElementForeground = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Fire", Color.FromRgb(253, 133, 101) },
        { "Ice", Color.FromRgb(176, 236, 249) },
        { "Lightning", Color.FromRgb(228, 134, 252) },
        { "Wind", Color.FromRgb(183, 234, 187) },
        { "Quantum", Color.FromRgb(196, 195, 248) },
        { "Imaginary", Color.FromRgb(255, 242, 156) },
        { "Physical", Color.FromRgb(236, 236, 236) }
    };

    private static readonly Dictionary<string, Color> ElementBackground = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Fire", Color.FromRgba(230, 41, 41, 128) },
        { "Ice", Color.FromRgba(38, 146, 211, 128) },
        { "Lightning", Color.FromRgba(184, 77, 211, 128) },
        { "Wind", Color.FromRgba(62, 177, 119, 128) },
        { "Quantum", Color.FromRgba(77, 69, 188, 128) },
        { "Imaginary", Color.FromRgba(245, 222, 53, 128) },
        { "Physical", Color.FromRgba(191, 195, 190, 128) }
    };

    public HsrCharListCardService(IImageRepository imageRepository,
        ILogger<HsrCharListCardService> logger,
        IApplicationMetrics metrics)
        : base(
            "Hsr CharList",
            imageRepository,
            logger,
            metrics,
            LoadFonts("Assets/Fonts/hsr.ttf", titleSize: 40, normalSize: 28, smallSize: 20))
    {
    }

    public override Task LoadStaticResourcesAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public override async Task RenderCardAsync(
        Image<Rgba32> background,
        ICardGenerationContext<IEnumerable<HsrCharacterInformation>> context,
        DisposableBag disposables,
        CancellationToken cancellationToken = default)
    {
        var charData = context.Data.ToList();

        Logger.LogInformation("Generating character list card for user {UserId} with {CharCount} characters",
            context.GameProfile.GameUid, charData.Count);

        var weaponImages = await charData.Where(x => x.Equip is not null).Select(x => x.Equip)
            .DistinctBy(x => x!.Id)
            .ToAsyncEnumerable()
            .Select(async (x, token) =>
            {
                var image = await LoadImageFromRepositoryAsync(x!.ToIconImageName(), disposables, token);
                return (x!.Id, Image: image);
            }).ToDictionaryAsync(x => x.Id, x => x.Image);

        var avatarImageTasks = charData.OrderByDescending(x => x.Level)
            .ThenByDescending(x => x.Rarity)
            .ThenBy(x => x.Name)
            .ToAsyncEnumerable()
            .Select(async (x, token) =>
            {
                var avatarImage = await LoadImageFromRepositoryAsync(x.ToAvatarImageName(), disposables, token);
                var styledImage = GetStyledCharacterImage(x, avatarImage, x.Equip is null ? null : weaponImages[x.Equip.Id]); ;
                disposables.Add(styledImage);
                return styledImage;
            })
            .ToListAsync();

        var charCountByElem = charData.GroupBy(x => x.Element!)
            .OrderBy(x => Array.IndexOf(Elements, x.Key))
            .Select(x => new { Element = x.Key, Count = x.Count() }).ToList();
        var charCountByRarity = charData.GroupBy(x => x.Rarity!.Value)
            .OrderBy(x => x.Key)
            .Select(x => new { Rarity = x.Key, Count = x.Count() }).ToList();

        var avatarImages = await avatarImageTasks;

        var layout =
            ImageUtility.CalculateGridLayout(avatarImages.Count, 300, 180, [120, 50, 50, 50]);

        background.Mutate(ctx =>
        {
            ctx.Resize(layout.OutputWidth, layout.OutputHeight + 50);
            ctx.Clear(Color.FromRgb(69, 69, 69));
            ctx.DrawText(new RichTextOptions(Fonts.Title)
            {
                Origin = new Vector2(50, 80),
                VerticalAlignment = VerticalAlignment.Bottom
            }, $"{context.GameProfile.Nickname} · TB {context.GameProfile.Level}", Color.White);

            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(50, 110),
                VerticalAlignment = VerticalAlignment.Bottom
            }, context.GameProfile.GameUid!, Color.White);

            foreach (var position in layout.ImagePositions)
            {
                var image = avatarImages[position.ImageIndex];
                ctx.DrawImage(image, new Point(position.X, position.Y), 1f);
            }

            var yOffset = layout.OutputHeight - 30;
            var xOffset = 50;
            foreach (var entry in charCountByElem)
            {
                var countSize = TextMeasurer.MeasureSize(entry.Count.ToString(),
                    new TextOptions(Fonts.Normal));
                var elemSize = TextMeasurer.MeasureSize(entry.Element, new TextOptions(Fonts.Normal));
                FontRectangle size = new(0, 0, countSize.Width + elemSize.Width + 20,
                    countSize.Height + elemSize.Height);
                EllipsePolygon foreground = new(new PointF(xOffset + 20, yOffset + 25), 10);
                ctx.DrawRoundedRectangleOverlay((int)size.Width + 50, 50, new PointF(xOffset, yOffset),
                    new RoundedRectangleOverlayStyle(ElementBackground[entry.Element], CornerRadius: 10));
                ctx.Fill(ElementForeground[entry.Element], foreground);
                ctx.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new PointF(xOffset + 40, yOffset + 26),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center
                }, entry.Element, Color.White);
                ctx.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new PointF(xOffset + 35 + size.Width, yOffset + 26),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                }, entry.Count.ToString(), Color.White);
                xOffset += (int)size.Width + 70;
            }

            foreach (var entry in charCountByRarity)
            {
                var countSize = TextMeasurer.MeasureSize(entry.Count.ToString(),
                    new TextOptions(Fonts.Normal));
                var elemSize =
                    TextMeasurer.MeasureSize($"{entry.Rarity} Star", new TextOptions(Fonts.Normal));
                FontRectangle size = new(0, 0, countSize.Width + elemSize.Width + 20,
                    countSize.Height + elemSize.Height);
                EllipsePolygon foreground = new(new PointF(xOffset + 20, yOffset + 25), 10);
                ctx.DrawRoundedRectangleOverlay((int)size.Width + 50, 50, new PointF(xOffset, yOffset),
                    new RoundedRectangleOverlayStyle(RarityColors[entry.Rarity - 2].WithAlpha(128), CornerRadius: 10));
                ctx.Fill(entry.Rarity == 5 ? Color.Gold : PurpleForegroundColor, foreground);
                ctx.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new PointF(xOffset + 40, yOffset + 26),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center
                }, $"{entry.Rarity} Star", Color.White);
                ctx.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new PointF(xOffset + 35 + size.Width, yOffset + 26),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                }, entry.Count.ToString(), Color.White);
                xOffset += (int)size.Width + 70;
            }
        });

        Logger.LogInformation("Completed character list card for user {UserId} with {CharCount} characters",
            context.GameProfile.GameUid, charData.Count);
    }

    private Image<Rgba32> GetStyledCharacterImage(HsrCharacterInformation charData, Image avatarImage,
        Image? weaponImage = null)
    {
        Image<Rgba32> background = new(300, 180);
        background.Mutate(ctx =>
        {
            ctx.Fill(RarityColors[charData.Rarity!.Value - 2], new RectangleF(0, 0, 150, 180));
            ctx.Fill(RarityColors[charData.Equip?.Rarity - 2 ?? 0], new RectangleF(150, 0, 150, 180));

            ctx.DrawImage(avatarImage, new Point(0, 0), 1f);
            if (weaponImage is not null)
                ctx.DrawImage(weaponImage, new Point(150, 0), 1f);

            var charLevelRect =
                TextMeasurer.MeasureSize($"Lv. {charData.Level}", new TextOptions(Fonts.Small!));
            ctx.DrawRoundedRectangleOverlay((int)charLevelRect.Width + 40, (int)charLevelRect.Height + 20,
                new PointF(-25, 105),
                new RoundedRectangleOverlayStyle(DarkOverlayColor, CornerRadius: 10));
            ctx.DrawText(new RichTextOptions(Fonts.Small!)
            {
                Origin = new PointF(5, 115 + charLevelRect.Height / 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            }, $"Lv. {charData.Level}", Color.White);

            if (charData.Rank > 0)
            {
                ctx.DrawRoundedRectangleOverlay(30, 30, new PointF(115, 110),
                    new RoundedRectangleOverlayStyle(
                        charData.Rank == 6 ? Color.Gold : NormalConstColor,
                        CornerRadius: 5));
                ctx.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new PointF(130, 125),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                    charData.Rank.ToString(),
                    charData.Rank == 6 ? GoldConstTextColor : Color.White);
            }

            if (charData.Equip is not null)
            {
                var weapLevelRect =
                    TextMeasurer.MeasureSize($"Lv. {charData.Equip.Level}", new TextOptions(Fonts.Small!));
                ctx.DrawRoundedRectangleOverlay((int)weapLevelRect.Width + 40, (int)weapLevelRect.Height + 20,
                    new PointF(285 - weapLevelRect.Width, 105),
                    new RoundedRectangleOverlayStyle(DarkOverlayColor, CornerRadius: 10));
                ctx.DrawText(new RichTextOptions(Fonts.Small!)
                {
                    Origin = new PointF(295 - weapLevelRect.Width / 2, 115 + weapLevelRect.Height / 2),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }, $"Lv. {charData.Equip.Level}", Color.White);

                if (charData.Equip.Rank > 0)
                {
                    ctx.DrawRoundedRectangleOverlay(30, 30, new PointF(155, 110),
                        new RoundedRectangleOverlayStyle(
                            charData.Equip.Rank == 5 ? Color.Gold : NormalConstColor,
                            CornerRadius: 5));
                    ctx.DrawText(new RichTextOptions(Fonts.Normal)
                    {
                        Origin = new PointF(170, 125),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                        charData.Equip.Rank.ToString(),
                        charData.Equip.Rank == 5 ? GoldConstTextColor : Color.White);
                }
            }

            ctx.DrawLine(OverlayColor, 2f, new PointF(150, -5), new PointF(150, 185));
            ctx.BoxBlur(2, new Rectangle(147, 0, 5, 180));

            ctx.Fill(Color.Black, new RectangleF(0, 146, 300, 30));
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new PointF(150, 161),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }, $"{charData.Name}", Color.White);

            ctx.ApplyRoundedCorners(15);
        });

        return background;
    }
}
