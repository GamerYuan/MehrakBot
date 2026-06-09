#region

using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Renderers;
using Mehrak.Application.Shared.Utility;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Models;
using Mehrak.Domain.User.Abstractions;
using Mehrak.GameApi.Genshin.Types;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Genshin.CharList;

public class GenshinCharListCardService : CardServiceBase<IEnumerable<GenshinBasicCharacterData>>
{
    private static readonly Color GoldBackgroundColor = Color.FromPixel(new Rgb24(183, 125, 76));
    private static readonly Color PurpleBackgroundColor = Color.FromPixel(new Rgb24(132, 104, 173));
    private static readonly Color BlueBackgroundColor = Color.FromPixel(new Rgb24(86, 130, 166));
    private static readonly Color GreenBackgroundColor = Color.FromPixel(new Rgb24(79, 135, 111));
    private static readonly Color WhiteBackgroundColor = Color.FromPixel(new Rgb24(128, 128, 130));

    private static readonly Color[] RarityColors =
    [
        WhiteBackgroundColor,
        GreenBackgroundColor,
        BlueBackgroundColor,
        PurpleBackgroundColor,
        GoldBackgroundColor
    ];

    private static readonly Color PurpleForegroundColor = Color.FromPixel(new Rgb24(204, 173, 255));

    private static readonly string[] Elements =
    [
        "Pyro", "Hydro", "Cryo", "Electro", "Anemo", "Geo", "Dendro"
    ];

    private static readonly Dictionary<string, Color> ElementForeground = new()
    {
        { "Pyro", Color.FromPixel(new Rgb24(244, 163, 111)) },
        { "Hydro", Color.FromPixel(new Rgb24(7, 229, 252)) },
        { "Cryo", Color.FromPixel(new Rgb24(203, 253, 253)) },
        { "Electro", Color.FromPixel(new Rgb24(222, 186, 255)) },
        { "Anemo", Color.FromPixel(new Rgb24(163, 238, 202)) },
        { "Geo", Color.FromPixel(new Rgb24(242, 213, 95)) },
        { "Dendro", Color.FromPixel(new Rgb24(172, 230, 40)) }
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
            var iconName = string.Format(FileNameFormat.Genshin.ElementName, element.ToLowerInvariant());
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
            NormalConstColor: Color.FromPixel(new Rgba32(69, 69, 69, 200)),
            GoldConstColor: Color.Gold,
            GoldConstTextColor: Color.FromPixel(new Rgb24(138, 101, 0)),
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

        var renderer = new CharacterModuleRenderer(moduleStyle);
        var layout =
            ImageUtility.CalculateGridLayout(avatarDataList.Count,
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
                var bc = entry.Rarity == 5 ? Color.Gold : PurpleForegroundColor;
                var module = renderer.RenderFooterModule($"{entry.Rarity} Star", entry.Count, bc);
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

            ctx.Paint(canvas =>
            {
                renderer.RenderHeader(canvas, outputWidth,
                    $"{context.GameProfile.Nickname!}·AR {context.GameProfile.Level}", context.GameProfile.GameUid!);

                foreach (var position in layout.ImagePositions)
                {
                    var (character, moduleData) = avatarDataList[position.ImageIndex];
                    renderer.Render(canvas, moduleData, new Point(position.X, position.Y),
                        ElementForeground.TryGetValue(character.Element ?? "", out var elementColor) ? elementColor : null);
                }

                CharacterModuleRenderer.RenderFooter(canvas, outputWidth, layout.OutputHeight, footerModules, disposables);
            });
        });

        Logger.LogInformation("Completed character list card for user {UserId} with {CharCount} characters",
            context.GameProfile.GameUid, charData.Count);
    }

    private static string GetWeaponKey(Weapon weapon)
    {
        return $"{weapon.Id}_{(weapon.Ascended.Value ? "Ascended" : "Normal")}";
    }

}
