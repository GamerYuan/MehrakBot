#region

using System.Numerics;
using Mehrak.Application.Renderers;
using Mehrak.Application.Renderers.Extensions;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Utility;
using Mehrak.Domain.Extensions;
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

    private static readonly Color BorderColor = Color.FromRgb(120, 120, 120);

    private static readonly string[] Elements =
        ["physical", "fire", "ice", "lightning", "wind", "quantum", "imaginary"];

    private static readonly Dictionary<string, Color> ElementBackground = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Fire", Color.FromRgb(255, 46, 46) },
        { "Ice", Color.FromRgb(38, 146, 211) },
        { "Lightning", Color.FromRgb(184, 77, 211) },
        { "Wind", Color.FromRgb(62, 177, 119) },
        { "Quantum", Color.FromRgb(136, 128, 255) },
        { "Imaginary", Color.FromRgb(245, 222, 53) },
        { "Physical", Color.FromRgb(191, 195, 190) }
    };

    private readonly Dictionary<string, Image> m_ElementIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Image> m_SmallElementIcons = new(StringComparer.OrdinalIgnoreCase);

    private Image m_WeaponPlaceholder = null!;

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

    public override async Task LoadStaticResourcesAsync(CancellationToken cancellationToken = default)
    {
        var weaponStream = await ImageRepository.DownloadFileToStreamAsync("hsr_lightcone_template", cancellationToken);
        m_WeaponPlaceholder = await Image.LoadAsync<Rgba32>(weaponStream, cancellationToken);
        m_WeaponPlaceholder.Mutate(ctx => ctx.Resize(150, 0, KnownResamplers.Bicubic));

        foreach (var element in Elements)
        {
            var iconName = $"hsr_element_{element.ToLowerInvariant()}";
            await using var stream = await ImageRepository.DownloadFileToStreamAsync(iconName, cancellationToken);
            using var image = await Image.LoadAsync(stream, cancellationToken);
            m_ElementIcons[element] = image.Clone(ctx => ctx.Resize(40, 0, KnownResamplers.Bicubic));
            m_SmallElementIcons[element] = image.Clone(ctx => ctx.Resize(30, 0, KnownResamplers.Bicubic));
        }
    }

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
                image.Mutate(ctx => ctx.Resize(150, 0, KnownResamplers.Bicubic));
                return (x!.Id, Image: image);
            }).ToDictionaryAsync(x => x.Id, x => x.Image);

        var moduleStyle = new CharacterModuleStyle(
            Fonts,
            RarityColors,
            NameColor: Color.White,
            LevelTextColor: Color.White,
            LevelOverlayColor: Color.Black,
            NormalConstColor: Color.FromRgba(69, 69, 69, 200),
            GoldConstColor: Color.Gold,
            GoldConstTextColor: Color.FromRgb(138, 101, 0),
            FooterTextColor: Color.White,
            PlaceholderWeaponIcon: m_WeaponPlaceholder);

        var avatarDataTask = charData
            .OrderByDescending(x => x.Level)
            .ThenBy(x => Elements.IndexOf(x.Element, StringComparer.OrdinalIgnoreCase))
            .ThenByDescending(x => x.Rarity)
            .ThenBy(x => x.Name)
            .ToAsyncEnumerable()
            .Select(async (x, token) =>
            {
                var avatarImage = await LoadImageFromRepositoryAsync(x.ToAvatarImageName(), disposables, token);
                var moduleData = new CharacterModuleData(
                    x.Name,
                    x.Level,
                    x.Rarity!.Value - 1,
                    avatarImage,
                    x.Rank,
                    Icon: m_SmallElementIcons[x.Element],
                    Weapon: x.Equip is null ? null : new WeaponModuleData(
                        x.Equip.Level,
                        x.Equip.Rarity - 1,
                        x.Equip.Rank,
                        weaponImages[x.Equip.Id]));
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
            }, $"{context.GameProfile.Nickname} · TB {context.GameProfile.Level}", Color.White);

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
                    ElementBackground.GetValueOrDefault(character.Element ?? "", Color.FromRgb(120, 120, 120)));
            }

            // Footer
            const int footerHeight = 100;
            const int footerX = 50;
            var footerWidth = outputWidth - 100;
            var footerY = layout.OutputHeight - layout.PaddingBottom + 20;

            var footerModules = new List<Image<Rgba32>>();
            foreach (var entry in charCountByRarity)
            {
                var borderColor = entry.Rarity == 5 ? Color.Gold : PurpleBackgroundColor;
                var module = renderer.RenderFooterModule($"{entry.Rarity} Star", entry.Count, borderColor);
                disposables.Add(module);
                footerModules.Add(module);
            }

            foreach (var entry in charCountByElem)
            {
                var module = renderer.RenderFooterModule(entry.Element.ToTitleCase(), entry.Count,
                    ElementBackground[entry.Element], m_ElementIcons[entry.Element]);
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
}
