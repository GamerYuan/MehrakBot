#region

using System.Numerics;
using Mehrak.Application.Renderers;
using Mehrak.Application.Renderers.Extensions;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Utility;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.GameApi.Genshin.Types;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Services.Genshin.CharList;

public class GenshinCharListCardService : CardServiceBase<IEnumerable<GenshinBasicCharacterData>>
{
    private static readonly Color GoldBackgroundColor = Color.FromRgb(183, 125, 76);
    private static readonly Color PurpleBackgroundColor = Color.FromRgb(132, 104, 173);
    private static readonly Color BlueBackgroundColor = Color.FromRgb(86, 130, 166);
    private static readonly Color GreenBackgroundColor = Color.FromRgb(79, 135, 111);
    private static readonly Color WhiteBackgroundColor = Color.FromRgb(128, 128, 130);

    private static readonly Color[] RarityColors =
    [
        WhiteBackgroundColor,
        GreenBackgroundColor,
        BlueBackgroundColor,
        PurpleBackgroundColor,
        GoldBackgroundColor
    ];

    private static readonly Color PurpleForegroundColor = Color.FromRgb(204, 173, 255);

    private static readonly Color NormalConstColor = Color.FromRgba(69, 69, 69, 200);
    private static readonly Color GoldConstTextColor = Color.FromRgb(138, 101, 0);

    private static readonly string[] Elements =
    [
        "Pyro", "Hydro", "Cryo", "Electro", "Anemo", "Geo", "Dendro"
    ];

    private static readonly Dictionary<string, Color> ElementForeground = new()
    {
        { "Pyro", Color.FromRgb(244, 163, 111) },
        { "Hydro", Color.FromRgb(7, 229, 252) },
        { "Cryo", Color.FromRgb(203, 253, 253) },
        { "Electro", Color.FromRgb(222, 186, 255) },
        { "Anemo", Color.FromRgb(163, 238, 202) },
        { "Geo", Color.FromRgb(242, 213, 95) },
        { "Dendro", Color.FromRgb(172, 230, 40) }
    };

    private static readonly Dictionary<string, Color> ElementBackground = new()
    {
        { "Pyro", Color.FromRgba(198, 90, 21, 128) },
        { "Hydro", Color.FromRgba(25, 156, 198, 128) },
        { "Cryo", Color.FromRgba(108, 192, 192, 128) },
        { "Electro", Color.FromRgba(177, 117, 217, 128) },
        { "Anemo", Color.FromRgba(56, 185, 145, 128) },
        { "Geo", Color.FromRgba(179, 132, 36, 128) },
        { "Dendro", Color.FromRgba(128, 175, 18, 128) }
    };

    public GenshinCharListCardService(IImageRepository imageRepository, ILogger<GenshinCharListCardService> logger, IApplicationMetrics metrics)
        : base(
            "Genshin CharList",
            imageRepository,
            logger,
            metrics,
            LoadFonts("Assets/Fonts/genshin.ttf", titleSize: 40, normalSize: 28, smallSize: 20))
    { }

    public override Task LoadStaticResourcesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public override async Task RenderCardAsync(
        Image<Rgba32> background,
        ICardGenerationContext<IEnumerable<GenshinBasicCharacterData>> context,
        DisposableBag disposables,
        CancellationToken cancellationToken = default)
    {
        var charData = context.Data.ToList();

        Logger.LogInformation("Generating character list card for user {UserId} with {CharCount} characters",
            context.GameProfile.GameUid, charData.Count);

        var weaponImages = await charData
            .Select(x => (Key: GetWeaponKey(x.Weapon), x.Weapon))
            .DistinctBy(x => x.Key)
            .ToAsyncEnumerable()
            .ToDictionaryAsync(
                (x, token) => ValueTask.FromResult(x.Key),
                async (x, token) =>
                {
                    Image image;
                    if (x.Weapon.Ascended.Value && await ImageRepository.FileExistsAsync(x.Weapon.ToAscendedImageName(), token))
                    {
                        image = await LoadImageFromRepositoryAsync(x.Weapon.ToAscendedImageName(), disposables, token);
                    }
                    else
                    {
                        if (x.Weapon.Ascended.Value)
                        {
                            Logger.LogInformation("Ascended icon not found for Weapon {Weapon}, falling back to default icon",
                                x.Weapon.Name);
                        }
                        image = await LoadImageFromRepositoryAsync(x.Weapon.ToBaseImageName(), disposables, token);
                    }

                    image.Mutate(ctx => ctx.Resize(150, 0, KnownResamplers.Bicubic));
                    return image;
                });
        var avatarImageTask = charData.OrderByDescending(x => x.Level)
            .ThenByDescending(x => x.Rarity)
            .ThenBy(x => x.Name)
            .ToAsyncEnumerable()
            .Select(async (x, token) =>
            {
                var avatarImage = await LoadImageFromRepositoryAsync(x.ToImageName(), disposables, token);
                var styledImage = GetStyledCharacterImage(x, avatarImage, weaponImages[GetWeaponKey(x.Weapon)]);
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

        var avatarImages = await avatarImageTask;

        var layout =
            ImageUtility.CalculateGridLayout(avatarImages.Count, 300, 180, [120, 50, 50, 50]);

        var outputWidth = layout.OutputWidth;
        var outputHeight = layout.OutputHeight + 50;
        if (background.Width != outputWidth || background.Height != outputHeight)
            background.Mutate(ctx => ctx.Resize(outputWidth, outputHeight));
        background.Mutate(ctx =>
        {
            ctx.Clear(Color.FromRgb(69, 69, 69));
            ctx.DrawText(new RichTextOptions(Fonts.Title)
            {
                Origin = new Vector2(50, 80),
                VerticalAlignment = VerticalAlignment.Bottom
            }, $"{context.GameProfile.Nickname}·AR {context.GameProfile.Level}", Color.White);

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
                    Origin = new Vector2(xOffset + 40, yOffset + 26),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center
                }, entry.Element, Color.White);
                ctx.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(xOffset + 35 + size.Width, yOffset + 26),
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
                    new RoundedRectangleOverlayStyle(RarityColors[entry.Rarity - 1].WithAlpha(128), CornerRadius: 10));
                ctx.Fill(entry.Rarity == 5 ? Color.Gold : PurpleForegroundColor, foreground);
                ctx.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(xOffset + 40, yOffset + 26),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center
                }, $"{entry.Rarity} Star", Color.White);
                ctx.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(xOffset + 35 + size.Width, yOffset + 26),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                }, entry.Count.ToString(), Color.White);
                xOffset += (int)size.Width + 70;
            }
        });

        Logger.LogInformation("Completed character list card for user {UserId} with {CharCount} characters",
            context.GameProfile.GameUid, charData.Count);
    }

    private Image<Rgba32> GetStyledCharacterImage(GenshinBasicCharacterData charData, Image avatarImage,
        Image weaponImage)
    {
        Image<Rgba32> background = new(300, 180);
        background.Mutate(ctx =>
        {
            ctx.Fill(RarityColors[charData.Rarity!.Value - 1], new RectangleF(0, 0, 150, 180));
            ctx.Fill(RarityColors[charData.Weapon.Rarity!.Value - 1], new RectangleF(150, 0, 150, 180));

            ctx.DrawImage(avatarImage, new Point(0, 0), 1f);
            ctx.DrawImage(weaponImage, new Point(150, 0), 1f);

            var charLevelRect =
                TextMeasurer.MeasureSize($"Lv. {charData.Level}", new TextOptions(Fonts.Small!));
            ctx.DrawRoundedRectangleOverlay((int)charLevelRect.Width + 40, (int)charLevelRect.Height + 20,
                new PointF(-25, 110),
                new RoundedRectangleOverlayStyle(DarkOverlayColor, CornerRadius: 10));
            ctx.DrawText(new RichTextOptions(Fonts.Small!)
            {
                Origin = new Vector2(5, 120 + charLevelRect.Height / 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            }, $"Lv. {charData.Level}", Color.White);

            if (charData.ActivedConstellationNum > 0)
            {
                ctx.DrawRoundedRectangleOverlay(30, 30, new PointF(115, 115),
                    new RoundedRectangleOverlayStyle(
                        charData.ActivedConstellationNum == 6 ? Color.Gold : NormalConstColor,
                        CornerRadius: 5));
                ctx.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(130, 130),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                    charData.ActivedConstellationNum.ToString()!,
                    charData.ActivedConstellationNum == 6 ? GoldConstTextColor : Color.White);
            }

            var weapLevelRect =
                TextMeasurer.MeasureSize($"Lv. {charData.Weapon.Level}", new TextOptions(Fonts.Small!));
            ctx.DrawRoundedRectangleOverlay((int)weapLevelRect.Width + 40, (int)weapLevelRect.Height + 20,
                new PointF(285 - weapLevelRect.Width, 110),
                new RoundedRectangleOverlayStyle(DarkOverlayColor, CornerRadius: 10));
            ctx.DrawText(new RichTextOptions(Fonts.Small)
            {
                Origin = new PointF(295 - weapLevelRect.Width / 2, 120 + weapLevelRect.Height / 2),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }, $"Lv. {charData.Weapon.Level}", Color.White);

            if (charData.Weapon.AffixLevel > 0)
            {
                ctx.DrawRoundedRectangleOverlay(30, 30, new PointF(155, 115),
                    new RoundedRectangleOverlayStyle(
                        charData.Weapon.AffixLevel == 5 ? Color.Gold : NormalConstColor,
                        CornerRadius: 5));
                ctx.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(170, 130),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                    charData.Weapon.AffixLevel.ToString()!,
                    charData.Weapon.AffixLevel == 5 ? GoldConstTextColor : Color.White);
            }

            ctx.DrawLine(OverlayColor, 2f, new PointF(150, -5), new PointF(150, 185));
            ctx.BoxBlur(2, new Rectangle(147, 0, 5, 180));

            ctx.Fill(Color.PeachPuff, new RectangleF(0, 150, 300, 30));
            ctx.DrawText(new RichTextOptions(charData.Name.Length >= 15 ? Fonts.Small : Fonts.Normal)
            {
                Origin = new Vector2(150, 165),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }, $"{charData.Name}", Color.Black);

            ctx.ApplyRoundedCorners(15);
        });

        return background;
    }

    private static string GetWeaponKey(Weapon weapon)
    {
        return $"{weapon.Id}_{(weapon.Ascended.Value ? "Ascended" : "Normal")}";
    }
}
