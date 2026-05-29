using System.Globalization;
using Mehrak.Application.Renderers;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Utility;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Models;
using Mehrak.Domain.User.Abstractions;
using Mehrak.GameApi.Zzz.Types;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Mehrak.Application.Services.Zzz.CharList;

public class ZzzCharListCardService : CardServiceBase<(IEnumerable<ZzzBasicAvatarData>, IEnumerable<ZzzBuddyData>)>
{
    private static readonly List<string> AttributeNames = [
        "physical", "fire", "ice", "electric", "ether", "frost", "auricink", "honededge"
    ];

    private static readonly Dictionary<string, Color> ElementForeground = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Physical", Color.FromRgb(255, 226, 0) },
        { "Fire", Color.FromRgb(254, 83, 26) },
        { "Ice", Color.FromRgb(126, 233, 232) },
        { "Electric", Color.FromRgb(0, 145, 217) },
        { "Ether", Color.FromRgb(122, 78, 204) },
    };

    private static readonly char[] RarityOrder = ['S', 'A'];

    private static readonly Color GoldBackgroundColor = Color.FromRgb(183, 125, 76);
    private static readonly Color PurpleBackgroundColor = Color.FromRgb(132, 104, 173);

    private static readonly TextInfo TextInfo = new CultureInfo("en-US", false).TextInfo;

    private static readonly Color[] RarityColors =
    [
        Color.FromRgb(128, 128, 130),
        Color.FromRgb(79, 135, 111),
        Color.FromRgb(86, 130, 166),
        PurpleBackgroundColor,
        GoldBackgroundColor,
    ];

    private readonly Dictionary<string, Image> m_ElementIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Image> m_SmallElementIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Image> m_ProfessionIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, Image> m_StarImages = [];

    public ZzzCharListCardService(IImageRepository imageRepository,
        ILogger<ZzzCharListCardService> logger,
        IApplicationMetrics metrics)
        : base(
            "Zzz CharList",
            imageRepository,
            logger,
            metrics,
            LoadFonts("Assets/Fonts/zzz.ttf", 40f, 28f, null, null))
    {
    }

    public override async Task LoadStaticResourcesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var attribute in AttributeNames)
        {
            await using var stream = await ImageRepository.DownloadFileToStreamAsync(
                string.Format(FileNameFormat.Zzz.AttributeName, attribute), cancellationToken);
            using var image = await Image.LoadAsync(stream, cancellationToken);
            m_ElementIcons[attribute] = image.Clone(x => x.Resize(40, 0, KnownResamplers.Bicubic));
            m_SmallElementIcons[attribute] = image.Clone(x => x.Resize(30, 0, KnownResamplers.Bicubic));
        }

        foreach (var professionId in Enumerable.Range(1, 6))
        {
            var iconName = string.Format(FileNameFormat.Zzz.ProfessionName, professionId);
            await using var stream = await ImageRepository.DownloadFileToStreamAsync(iconName, cancellationToken);
            var image = await Image.LoadAsync(stream, cancellationToken);
            image.Mutate(x => x.Resize(40, 0, KnownResamplers.Bicubic));
            m_ProfessionIcons[StatUtils.GetProfessionNameFromId(professionId)] = image;
        }

        foreach (var star in Enumerable.Range(1, 5))
        {
            var iconName = string.Format(FileNameFormat.Zzz.WeaponStarName, star);
            await using var stream = await ImageRepository.DownloadFileToStreamAsync(iconName, cancellationToken);
            var image = await Image.LoadAsync(stream, cancellationToken);
            image.Mutate(x => x.Resize(65, 0, KnownResamplers.Bicubic));
            m_StarImages[star] = image;
        }
    }

    protected override Image<Rgba32> CreateBackground()
    {
        return new Image<Rgba32>(1, 1);
    }

    public override async Task RenderCardAsync(
        Image<Rgba32> background,
        ICardGenerationContext<(IEnumerable<ZzzBasicAvatarData>, IEnumerable<ZzzBuddyData>)> context,
        DisposableBag disposables,
        CancellationToken cancellationToken = default)
    {
        var charData = context.Data.Item1.ToList();
        var buddyData = context.Data.Item2.ToList();

        Logger.LogInformation("Generating character list card for user {UserId} with {CharCount} characters",
            context.UserId, charData.Count);

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
            PlaceholderWeaponIcon: null,
            DrawWeapon: false,
            AvatarBorderColor: Color.Black);

        var renderer = new CharacterModuleRenderer(moduleStyle);

        var charModuleDataTask = charData
            .OrderByDescending(x => x.Level)
            .ThenBy(x => x.ElementType)
            .ThenByDescending(x => x.SubElementType)
            .ThenByDescending(x => x.Rarity)
            .ThenBy(x => x.Name)
            .ToAsyncEnumerable()
            .Select(async (x, token) =>
            {
                await using var stream = await ImageRepository.DownloadFileToStreamAsync(x.ToImageName(), token);
                var image = await Image.LoadAsync(stream, token);
                disposables.Add(image);
                return (x, new CharacterModuleData(
                    x.Name,
                    x.Level,
                    MapRarity(x.Rarity),
                    image,
                    x.Rank,
                    Icon: m_SmallElementIcons.GetValueOrDefault(StatUtils.GetElementNameFromId(x.ElementType, x.SubElementType)),
                    Weapon: null));
            })
            .ToListAsync(cancellationToken: cancellationToken);

        var buddyModuleDataTask = buddyData
            .OrderByDescending(x => x.Level)
            .ThenByDescending(x => x.Star)
            .ThenBy(x => x.Name)
            .ToAsyncEnumerable()
            .Select(async (x, token) =>
            {
                await using var stream = await ImageRepository.DownloadFileToStreamAsync(x!.ToImageName(), token);
                var image = await Image.LoadAsync(stream, token);
                image.Mutate(ctx => ctx.Crop(new Rectangle(45, 20, image.Width - 45, image.Height - 20)));
                disposables.Add(image);
                return new CharacterModuleData(
                    x.Name,
                    x.Level,
                    MapRarity(x.Rarity),
                    image,
                    ConstellationNum: 0,
                    Icon: m_StarImages.GetValueOrDefault(x.Star),
                    Weapon: null);
            })
            .ToListAsync(cancellationToken: cancellationToken);

        var charModules = await charModuleDataTask;
        var buddyModules = await buddyModuleDataTask;

        var layout = ImageUtility.CalculateSplitGridLayout(
            charModules.Count,
            buddyModules.Count,
            renderer.CanvasSize.Width,
            renderer.CanvasSize.Height,
            [170, 50, 120, 50],
            20,
            80);

        var charCountByElem = charData
            .GroupBy(x => x.ElementType)
            .OrderBy(x => x.Key)
            .Select(x => new { Element = ToTitleCase(StatUtils.GetElementNameFromId(x.Key, 0)), Count = x.Count() })
            .ToList();

        var charCountByProfession = charData
            .GroupBy(x => x.AvatarProfession)
            .OrderBy(x => x.Key)
            .Select(x => new { Profession = StatUtils.GetProfessionNameFromId(x.Key), Count = x.Count() })
            .ToList();

        var charCountByRarity = charData
            .GroupBy(x => x.Rarity.ToUpperInvariant())
            .OrderBy(x => RarityOrder.IndexOf(x.Key[0]))
            .Select(x => new { Rarity = $"{x.Key}-Rank", Count = x.Count() })
            .ToList();

        var outputWidth = layout.OutputWidth;
        var outputHeight = layout.OutputHeight + 50;
        if (background.Width != outputWidth || background.Height != outputHeight)
            background.Mutate(ctx => ctx.Resize(outputWidth, outputHeight));

        background.Mutate(ctx =>
        {
            ctx.Clear(Color.FromRgb(27, 27, 27));

            renderer.RenderHeader(ctx, outputWidth,
                $"{context.GameProfile.Nickname} · IK {context.GameProfile.Level}", context.GameProfile.GameUid!);

            for (var i = 0; i < charModules.Count + buddyModules.Count; i++)
            {
                var pos = layout.ImagePositions[i];
                if (i < charModules.Count)
                {
                    (var character, var moduleData) = charModules[i];
                    renderer.Render(ctx, moduleData, new Point(pos.X, pos.Y),
                        ElementForeground.TryGetValue(StatUtils.GetElementNameFromId(character.ElementType, 0), out var fgColor) ? fgColor : null);
                }
                else
                {
                    renderer.Render(ctx, buddyModules[i - charModules.Count], new Point(pos.X, pos.Y), Color.LightGray);
                }
            }

            var footerModules = new List<Image<Rgba32>>();

            foreach (var entry in charCountByRarity)
            {
                var bc = entry.Rarity == "S-Rank" ? GoldBackgroundColor : PurpleBackgroundColor;
                var module = renderer.RenderFooterModule(entry.Rarity, entry.Count, bc);
                disposables.Add(module);
                footerModules.Add(module);
            }

            foreach (var entry in charCountByElem)
            {
                var module = renderer.RenderFooterModule(entry.Element, entry.Count, ElementForeground.GetValueOrDefault(entry.Element), m_ElementIcons.GetValueOrDefault(entry.Element));
                disposables.Add(module);
                footerModules.Add(module);
            }

            foreach (var entry in charCountByProfession)
            {
                var module = renderer.RenderFooterModule(entry.Profession, entry.Count, Color.White, m_ProfessionIcons.GetValueOrDefault(entry.Profession));
                disposables.Add(module);
                footerModules.Add(module);
            }

            CharacterModuleRenderer.RenderFooter(ctx, outputWidth, layout.OutputHeight, footerModules, disposables);
        });

        Logger.LogInformation("Completed character list card for user {UserId} with {CharCount} characters",
            context.UserId, charData.Count);
    }

    private static int MapRarity(string rarity) => rarity.ToUpperInvariant() switch
    {
        "S" => 5,
        "A" => 4,
        "B" => 3,
        _ => 1,
    };

    private static string ToTitleCase(string str)
    {
        return TextInfo.ToTitleCase(str.ToLowerInvariant());
    }
}
