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

    private static readonly Color BorderColor = Color.FromRgb(120, 120, 120);

    private static readonly Color[] RarityColors =
    [
        WhiteBackgroundColor,
        GreenBackgroundColor,
        BlueBackgroundColor,
        PurpleBackgroundColor,
        GoldBackgroundColor
    ];

    private static readonly Color PurpleForegroundColor = Color.FromRgb(204, 173, 255);

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

    private readonly Dictionary<string, Image> m_ElementIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Image> m_SmallElementIcons = new(StringComparer.OrdinalIgnoreCase);

    public GenshinCharListCardService(IImageRepository imageRepository, ILogger<GenshinCharListCardService> logger, IApplicationMetrics metrics)
        : base(
            "Genshin CharList",
            imageRepository,
            logger,
            metrics,
            LoadFonts("Assets/Fonts/genshin.ttf", titleSize: 40, normalSize: 28, smallSize: 20))
    { }

    public override async Task LoadStaticResourcesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var element in Elements)
        {
            var iconName = $"genshin_element_{element.ToLower()}";
            await using var stream = await ImageRepository.DownloadFileToStreamAsync(iconName, cancellationToken);
            using var image = await Image.LoadAsync(stream, cancellationToken);
            m_ElementIcons[element] = image.Clone(ctx => ctx.Resize(40, 0, KnownResamplers.Bicubic));
            m_SmallElementIcons[element] = image.Clone(ctx => ctx.Resize(30, 0, KnownResamplers.Bicubic));
        }
    }

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
                }, cancellationToken: cancellationToken);

        var moduleStyle = new CharacterModuleStyle(
            Fonts,
            RarityColors,
            NameColor: Color.White,
            LevelTextColor: Color.Black,
            LevelOverlayColor: Color.PeachPuff,
            NormalConstColor: Color.FromRgba(69, 69, 69, 200),
            GoldConstColor: Color.Gold,
            GoldConstTextColor: Color.FromRgb(138, 101, 0),
            FooterTextColor: Color.White);

        var avatarDataTask = charData
            .OrderByDescending(x => x.Level)
            .ThenBy(x => Elements.IndexOf(x.Element, StringComparer.OrdinalIgnoreCase))
            .ThenByDescending(x => x.Rarity)
            .ThenBy(x => x.Name)
            .ToAsyncEnumerable()
            .Select(async (x, token) =>
            {
                var avatarImage = await LoadImageFromRepositoryAsync(x.ToImageName(), disposables, token);
                var moduleData = new CharacterModuleData(
                    x.Name,
                    x.Level!.Value,
                    x.Rarity!.Value,
                    avatarImage,
                    x.ActivedConstellationNum,
                    Icon: m_SmallElementIcons.TryGetValue(x.Element!, out var value) ? value : null,
                    Weapon: new WeaponModuleData(
                        x.Weapon.Level!.Value,
                        x.Weapon.Rarity!.Value,
                        x.Weapon.AffixLevel,
                        weaponImages[GetWeaponKey(x.Weapon)]));
                return (Character: x, ModuleData: moduleData);
            })
            .ToListAsync(cancellationToken: cancellationToken);

        var charCountByElem = charData.GroupBy(x => x.Element!)
            .OrderBy(x => Array.IndexOf(Elements, x.Key))
            .Select(x => new { Element = x.Key, Count = x.Count() }).ToList();
        var charCountByRarity = charData.GroupBy(x => x.Rarity!.Value)
            .OrderBy(x => x.Key)
            .Select(x => new { Rarity = x.Key, Count = x.Count() }).ToList();

        var avatarDataList = await avatarDataTask;

        var layout =
            ImageUtility.CalculateGridLayout(avatarDataList.Count,
                CharacterModuleRenderer.CanvasSize.Width, CharacterModuleRenderer.CanvasSize.Height, [170, 50, 120, 50]);

        var outputWidth = layout.OutputWidth;
        var outputHeight = layout.OutputHeight + 50;
        if (background.Width != outputWidth || background.Height != outputHeight)
            background.Mutate(ctx => ctx.Resize(outputWidth, outputHeight));

        var renderer = new CharacterModuleRenderer(moduleStyle);
        background.Mutate(ctx =>
        {
            ctx.Clear(Color.FromRgb(27, 27, 27));

            // Header with rounded border
            const int headerHeight = 120;
            const int headerX = 50;
            var headerWidth = outputWidth - 100;

            ctx.DrawRoundedRectangleOverlay(headerWidth, headerHeight, new PointF(headerX, 25),
                new RoundedRectangleOverlayStyle(Color.Transparent, BorderColor, BorderWidth: 2, CornerRadius: 15));

            ctx.DrawText(new RichTextOptions(Fonts.Title)
            {
                Origin = new Vector2(headerX + 20, 50),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            }, $"{context.GameProfile.Nickname}·AR {context.GameProfile.Level}", Color.White);

            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(headerX + 20, 105),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            }, $"UID: {context.GameProfile.GameUid}", Color.White);

            foreach (var position in layout.ImagePositions)
            {
                var (character, moduleData) = avatarDataList[position.ImageIndex];
                renderer.Render(ctx, moduleData, new Point(position.X, position.Y),
                    ElementForeground.TryGetValue(character.Element ?? "", out var elementColor) ? elementColor : null);
            }

            // Footer
            const int footerHeight = 100;
            const int footerX = 50;
            var footerWidth = outputWidth - 100;
            var footerY = layout.OutputHeight - layout.PaddingBottom + 20;

            var footerModules = new List<Image<Rgba32>>();
            foreach (var entry in charCountByRarity)
            {
                var borderColor = entry.Rarity == 5 ? Color.Gold : PurpleForegroundColor;
                var module = renderer.RenderFooterModule($"{entry.Rarity} Star", entry.Count, borderColor);
                disposables.Add(module);
                footerModules.Add(module);
            }

            foreach (var entry in charCountByElem)
            {
                m_ElementIcons.TryGetValue(entry.Element, out var icon);
                var module = renderer.RenderFooterModule(entry.Element, entry.Count, ElementForeground[entry.Element], icon);
                disposables.Add(module);
                footerModules.Add(module);
            }

            const int moduleH = 70;
            const int spacing = 10;
            const int footerPadding = 20;
            var totalModuleWidth = footerModules.Sum(m => m.Width) + (footerModules.Count - 1) * spacing + footerPadding * 2;
            var scale = 1f;
            if (totalModuleWidth > footerWidth)
            {
                scale = (float)footerWidth / totalModuleWidth;
                for (var i = 0; i < footerModules.Count; i++)
                {
                    var oldModule = footerModules[i];
                    var newModule = oldModule.Clone(ctx => ctx.Resize((int)(oldModule.Width * scale), (int)(moduleH * scale)));
                    disposables.Add(newModule);
                    footerModules[i] = newModule;
                }
            }

            var scaledSpacing = spacing * scale;
            var scaledFooterPadding = footerPadding * scale;
            var totalScaledWidth = footerModules.Sum(m => m.Width) + (footerModules.Count - 1) * scaledSpacing + scaledFooterPadding * 2;
            var moduleStartX = footerX + (footerWidth - totalScaledWidth) / 2f + scaledFooterPadding;
            var moduleStartY = footerY + (footerHeight - moduleH * scale) / 2f;

            ctx.DrawRoundedRectangleOverlay(footerWidth, footerHeight, new PointF(footerX, footerY),
                new RoundedRectangleOverlayStyle(Color.Transparent, BorderColor, BorderWidth: 2, CornerRadius: 15));

            var currentX = moduleStartX;
            for (var i = 0; i < footerModules.Count; i++)
            {
                ctx.DrawImage(footerModules[i], new Point((int)currentX, (int)moduleStartY), 1f);
                currentX += footerModules[i].Width + scaledSpacing;
            }
        });

        Logger.LogInformation("Completed character list card for user {UserId} with {CharCount} characters",
            context.GameProfile.GameUid, charData.Count);
    }

    private static string GetWeaponKey(Weapon weapon)
    {
        return $"{weapon.Id}_{(weapon.Ascended.Value ? "Ascended" : "Normal")}";
    }

}
