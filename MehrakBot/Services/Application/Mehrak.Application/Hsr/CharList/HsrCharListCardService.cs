#region

using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Renderers;
using Mehrak.Application.Shared.Utility;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Models;
using Mehrak.Domain.Shared.Extensions;
using Mehrak.Domain.User.Abstractions;
using Mehrak.GameApi.Hsr.Types;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Hsr.CharList;

internal class HsrCharListCardService : CardServiceBase<IEnumerable<HsrCharacterInformation>>
{
    private static readonly Color GoldBackgroundColor = Color.ParseHex("BC8F60");
    private static readonly Color PurpleBackgroundColor = Color.ParseHex("7651B3");
    private static readonly Color BlueBackgroundColor = Color.FromPixel(new Rgb24(90, 131, 187));
    private static readonly Color WhiteBackgroundColor = Color.FromPixel(new Rgb24(128, 128, 130));

    private static readonly Color[] RarityColors =
    [
        WhiteBackgroundColor,
        BlueBackgroundColor,
        PurpleBackgroundColor,
        GoldBackgroundColor
    ];

    private static readonly string[] Elements =
        ["physical", "fire", "ice", "lightning", "wind", "quantum", "imaginary"];

    private static readonly Dictionary<string, Color> ElementBackground = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Fire", Color.FromPixel(new Rgb24(255, 46, 46)) },
        { "Ice", Color.FromPixel(new Rgb24(38, 146, 211)) },
        { "Lightning", Color.FromPixel(new Rgb24(184, 77, 211)) },
        { "Wind", Color.FromPixel(new Rgb24(62, 177, 119)) },
        { "Quantum", Color.FromPixel(new Rgb24(136, 128, 255)) },
        { "Imaginary", Color.FromPixel(new Rgb24(245, 222, 53)) },
        { "Physical", Color.FromPixel(new Rgb24(191, 195, 190)) }
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
        await using var weaponStream = await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hsr.LightconeTemplateName, cancellationToken);
        m_WeaponPlaceholder = await Image.LoadAsync<Rgba32>(weaponStream, cancellationToken);
        m_WeaponPlaceholder.Mutate(ctx => ctx.Resize(150, 0, KnownResamplers.Bicubic));

        foreach (var element in Elements)
        {
            var iconName = string.Format(FileNameFormat.Hsr.ElementName, element.ToLowerInvariant());
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

        // Load all distinct weapon images in parallel
        var weaponEntries = charData.Where(x => x.Equip is not null).Select(x => x.Equip!)
            .DistinctBy(x => x.Id)
            .ToList();

        var weaponTasks = weaponEntries.ToDictionary(
            x => x.Id,
            x => LoadWeaponImageAsync(x, disposables, cancellationToken));

        await Task.WhenAll(weaponTasks.Values);

        var weaponImages = weaponTasks.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Result);

        var moduleStyle = new CharacterModuleStyle(
            Fonts,
            RarityColors,
            NameColor: Color.White,
            LevelTextColor: Color.White,
            LevelOverlayColor: Color.Black,
            NormalConstColor: Color.FromPixel(new Rgba32(69, 69, 69, 200)),
            GoldConstColor: Color.Gold,
            GoldConstTextColor: Color.FromPixel(new Rgb24(138, 101, 0)),
            FooterTextColor: Color.White,
            PlaceholderWeaponIcon: m_WeaponPlaceholder);

        var sortedCharData = charData
            .OrderByDescending(x => x.Level)
            .ThenBy(x => Elements.IndexOf(x.Element, StringComparer.OrdinalIgnoreCase))
            .ThenByDescending(x => x.Rarity)
            .ThenBy(x => x.Name)
            .ToList();

        var avatarTasks = sortedCharData.Select(async x =>
        {
            var avatarImage = await LoadImageFromRepositoryAsync(x.ToAvatarImageName(), disposables, cancellationToken);
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
        }).ToList();

        var charCountByElem = charData.GroupBy(x => x.Element!)
            .OrderBy(x => Array.IndexOf(Elements, x.Key))
            .Select(x => new { Element = x.Key, Count = x.Count() }).ToList();
        var charCountByRarity = charData.GroupBy(x => x.Rarity!.Value)
            .OrderBy(x => x.Key)
            .Select(x => new { Rarity = x.Key, Count = x.Count() }).ToList();

        var avatarDataList = await Task.WhenAll(avatarTasks);

        var renderer = new CharacterModuleRenderer(moduleStyle);
        var layout =
            ImageUtility.CalculateGridLayout(avatarDataList.Length,
                renderer.CanvasSize.Width, renderer.CanvasSize.Height, [170, 50, 120, 50]);

        var outputWidth = layout.OutputWidth;
        var outputHeight = layout.OutputHeight + 50;
        if (background.Width != outputWidth || background.Height != outputHeight)
            background.Mutate(ctx => ctx.Resize(outputWidth, outputHeight));
        background.Mutate(ctx =>
        {
            ctx.Paint(canvas => canvas.Fill(Brushes.Solid(Color.FromPixel(new Rgb24(27, 27, 27))), new Rectangle(0, 0, background.Width, background.Height)));

            var footerModules = new List<Image<Rgba32>>();
            foreach (var entry in charCountByRarity.OrderByDescending(x => x.Rarity))
            {
                var bc = entry.Rarity == 5 ? Color.Gold : PurpleBackgroundColor;
                var module = renderer.RenderFooterModule($"{entry.Rarity} Star", entry.Count, bc);
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

            ctx.Paint(canvas =>
            {
                renderer.RenderHeader(canvas, outputWidth,
                    $"{context.GameProfile.Nickname!} · TB {context.GameProfile.Level}", context.GameProfile.GameUid!);

                foreach (var position in layout.ImagePositions)
                {
                    var (character, moduleData) = avatarDataList[position.ImageIndex];
                    renderer.Render(canvas, moduleData, new Point(position.X, position.Y),
                        ElementBackground.GetValueOrDefault(character.Element ?? "", Color.FromPixel(new Rgb24(120, 120, 120))));
                }

                CharacterModuleRenderer.RenderFooter(canvas, outputWidth, layout.OutputHeight, footerModules, disposables);
            });
        });

        Logger.LogInformation("Completed character list card for user {UserId} with {CharCount} characters",
            context.GameProfile.GameUid, charData.Count);
    }

    private async Task<Image> LoadWeaponImageAsync(Equip weapon, DisposableBag disposables, CancellationToken cancellationToken)
    {
        var image = await LoadImageFromRepositoryAsync(weapon.ToIconImageName(), disposables, cancellationToken);
        image.Mutate(ctx => ctx.Resize(150, 0, KnownResamplers.Bicubic));
        return image;
    }
}
