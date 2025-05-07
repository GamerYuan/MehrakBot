#region

using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Repositories;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace MehrakCore.Services.Genshin;

public class GenshinCharacterCardService : ICharacterCardService<GenshinCharacterInformation>
{
    private readonly ImageRepository m_ImageRepository;
    private readonly ILogger<GenshinCharacterCardService> m_Logger;

    private readonly FontFamily m_FontFamily;
    private readonly Dictionary<int, Image> m_StatImages;

    private const string BasePath = "genshin_{0}";
    private const string StatsPath = "genshin_stats_{0}.png";

    public GenshinCharacterCardService(ImageRepository imageRepository, ILogger<GenshinCharacterCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_Logger = logger;

        var collection = new FontCollection();
        m_FontFamily = collection.Add("Fonts/zh-cn.ttf");

        int[] statIds =
            [1, 2, 3, 4, 5, 6, 7, 8, 9, 20, 22, 23, 26, 27, 28, 30, 40, 41, 42, 43, 44, 45, 46, 2000, 2001, 2002];

        m_Logger.LogDebug("Loading {Count} stat icons", statIds.Length);
        var statImageTasks = statIds.Select(async x =>
        {
            try
            {
                var path = string.Format(StatsPath, x);
                m_Logger.LogTrace("Downloading stat icon {StatId}: {Path}", x, path);
                var imageBytes = await m_ImageRepository.DownloadFileAsBytesAsync(path);
                var image = Image.Load(imageBytes);
                image.Mutate(ctx => ctx.Resize(new Size(48, 0), KnownResamplers.Bicubic, true));
                return new KeyValuePair<int, Image>(x, image);
            }
            catch (Exception ex)
            {
                m_Logger.LogError(ex, "Failed to load stat icon {StatId}", x);
                throw;
            }
        }).ToList();

        var results = Task.WhenAll(statImageTasks).Result;
        m_StatImages = results.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        m_Logger.LogInformation(
            "Resources initialized successfully with {Count} icons.",
            m_StatImages.Count);

        m_Logger.LogInformation("GenshinCharacterCardService initialized");
    }

    public async Task<Stream> GenerateCharacterCardAsync(GenshinCharacterInformation charInfo)
    {
        m_Logger.LogInformation("Generating character card for {CharacterName} (ID: {CharacterId})",
            charInfo.Base.Name, charInfo.Base.Id);

        var disposableResources = new List<IDisposable>();

        try
        {
            m_Logger.LogDebug("Fetching background image for {Element} character card", charInfo.Base.Element);
            var overlay = Image.Load(await m_ImageRepository.DownloadFileAsBytesAsync($"bg.png"));
            disposableResources.Add(overlay);

            var background = new Image<Rgba32>(3240, 1080);
            disposableResources.Add(background);

            m_Logger.LogDebug("Loading character portrait for {CharacterId}", charInfo.Base.Id);
            var characterPortrait =
                Image.Load<Rgba32>(
                    await m_ImageRepository.DownloadFileAsBytesAsync(string.Format(BasePath, charInfo.Base.Id)));
            disposableResources.Add(characterPortrait);

            m_Logger.LogDebug("Loading weapon image for {WeaponId} ({WeaponName})",
                charInfo.Base.Weapon.Id, charInfo.Base.Weapon.Name);
            var weaponImage =
                Image.Load<Rgba32>(
                    await m_ImageRepository.DownloadFileAsBytesAsync(string.Format(BasePath, charInfo.Base.Weapon.Id)));
            disposableResources.Add(weaponImage);

            m_Logger.LogDebug("Loading {Count} constellation icons", charInfo.Constellations.Count);
            var constellationIcons =
                await Task.WhenAll(charInfo.Constellations.AsParallel().Select(async x =>
                {
                    var image = Image.Load(
                        await m_ImageRepository.DownloadFileAsBytesAsync(string.Format(BasePath, x.Id)));
                    disposableResources.Add(image);
                    return (Active: x.IsActived.GetValueOrDefault(false), Image: image);
                }).Reverse());

            m_Logger.LogDebug("Loading {Count} skill icons", charInfo.Skills.Count);
            var skillIcons = await Task.WhenAll(charInfo.Skills.AsParallel().Select(async x =>
            {
                var image = Image.Load(
                    await m_ImageRepository.DownloadFileAsBytesAsync(string.Format(BasePath, x.SkillId)));
                disposableResources.Add(image);
                return (Level: x.Level.GetValueOrDefault(1), Image: image);
            }).Take(3).Reverse());

            m_Logger.LogDebug("Processing {Count} relic images", charInfo.Relics.Count);
            var relics = await Task.WhenAll(charInfo.Relics.AsParallel().Select(async x =>
            {
                var relicImage = await CreateRelicSlotImage(x);
                disposableResources.Add(relicImage);
                return relicImage;
            }));

            m_Logger.LogTrace("Compositing character card image");

            background.Mutate(ctx =>
            {
                ctx.Fill(GetBackgroundColor(charInfo.Base.Element));
                ctx.DrawImage(overlay, PixelColorBlendingMode.Overlay, 1f);

                var titleFont = m_FontFamily.CreateFont(64, FontStyle.Regular);
                var font = m_FontFamily.CreateFont(40, FontStyle.Regular);
                var textColor = Color.White;

                ctx.DrawText(charInfo.Base.Name, titleFont, textColor, new PointF(100, 80));
                ctx.DrawText($"Lv. {charInfo.Base.Level}", font, textColor, new PointF(100, 160));
                ctx.DrawImage(characterPortrait, new Point(-50, 60), 1f);

                for (int i = 0; i <= 2; i++)
                {
                    var skill = skillIcons[i];
                    skill.Image.Mutate(x => x.Resize(new Size(150, 0), KnownResamplers.Bicubic, true));
                    ctx.DrawImage(skill.Image, new Point(40, 840 - i * 200), 1f);
                    var polygon = new EllipsePolygon(110, 980 - i * 200, 30);
                    ctx.Fill(Color.DarkGray, polygon);
                    ctx.DrawText(skill.Level.ToString(), font, textColor,
                        new PointF(100, 960 - i * 200));
                }

                for (int i = 0; i < constellationIcons.Length; i++)
                {
                    var constellation = constellationIcons[i];
                    constellation.Image.Mutate(x => x.Resize(new Size(100, 0), KnownResamplers.Bicubic, true));
                    if (!constellation.Active)
                        constellation.Image.Mutate(x => x.Brightness(0.65f));
                    ctx.DrawImage(constellation.Image, new Point(1000, 960 - i * 100), 1f);
                }

                weaponImage.Mutate(x => x.Resize(new Size(400, 0), KnownResamplers.Bicubic, true));
                ctx.DrawImage(weaponImage, new Point(1100, 0), 1f);
                ctx.DrawText(charInfo.Base.Weapon.Name, font, textColor, new PointF(1600, 80));
                ctx.DrawText('R' + charInfo.Base.Weapon.AffixLevel!.Value.ToString(), font, textColor,
                    new PointF(1600, 160));
                ctx.DrawText($"Lv. {charInfo.Weapon.Level}", font, textColor, new PointF(1700, 160));
                ctx.DrawImage(m_StatImages[charInfo.Weapon.MainProperty.PropertyType!.Value], new Point(1600, 236),
                    1f);
                ctx.DrawText(charInfo.Weapon.MainProperty.Final, font, textColor, new PointF(1664, 240));
                if (charInfo.Weapon.SubProperty != null)
                {
                    ctx.DrawImage(m_StatImages[charInfo.Weapon.SubProperty.PropertyType!.Value], new Point(1800, 236),
                        1f);
                    ctx.DrawText(charInfo.Weapon.SubProperty.Final, font, textColor, new PointF(1864, 240));
                }

                var stats = charInfo.BaseProperties.Take(3).Concat(charInfo.SelectedProperties)
                    .DistinctBy(x => x.PropertyType)
                    .Where(x => float.Parse(x.Final.TrimEnd('%')) >
                                StatMappingUtility.GetDefaultValue(x.PropertyType!.Value)).ToArray();
                var spacing = 560 / stats.Length;

                for (int i = 0; i < stats.Length; i++)
                {
                    var stat = stats[i];
                    var y = 480 + spacing * i;
                    ctx.DrawImage(m_StatImages[stat.PropertyType!.Value], new Point(1200, y - 4), 1f);
                    ctx.DrawText(StatMappingUtility.Mapping[stat.PropertyType!.Value], font, textColor,
                        new PointF(1264, y));
                    ctx.DrawText(stat.Final, font, textColor, new PointF(1800, y));
                }

                for (int i = 0; i < relics.Length; i++)
                {
                    var relic = relics[i];
                    ctx.DrawImage(relic, new Point(2200, 50 + i * 200), 1f);
                }
            });

            m_Logger.LogDebug("Saving character card to stream");
            var stream = new MemoryStream();
            await background.SaveAsJpegAsync(stream);
            stream.Position = 0;

            m_Logger.LogInformation("Successfully generated character card for {CharacterName}", charInfo.Base.Name);
            return stream;
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Failed to generate character card for {CharacterName} (ID: {CharacterId})",
                charInfo.Base.Name, charInfo.Base.Id);
            throw;
        }
        finally
        {
            foreach (var resource in disposableResources) resource.Dispose();
        }
    }

    private async Task<Image<Rgba32>> CreateRelicSlotImage(Relic relic)
    {
        m_Logger.LogTrace("Creating relic slot image for {RelicId}", relic.Id);
        try
        {
            var path = string.Format(BasePath, relic.Id);
            m_Logger.LogTrace("Loading relic image from {Path}", path);

            var relicImage = Image.Load<Rgba32>(
                await m_ImageRepository.DownloadFileAsBytesAsync(path));
            relicImage.Mutate(x => x.Resize(new Size(0, 100), KnownResamplers.Bicubic, true));

            var template = new Image<Rgba32>(800, 160);
            template.Mutate(ctx =>
            {
                ctx.Fill(new Rgba32(255, 255, 255, 0.25f));
                ctx.DrawImage(relicImage, new Point(50, 0), 1f);
                ctx.DrawImage(m_StatImages[relic.MainProperty.PropertyType!.Value], new Point(20, 106), 1f);
                var font = m_FontFamily.CreateFont(40);
                var smallFont = m_FontFamily.CreateFont(28);
                ctx.DrawText(relic.MainProperty.Value, font, Color.White, new PointF(84, 110));
                ctx.DrawText($"+{relic.Level!.Value}", smallFont, Color.White, new PointF(140, 80));

                for (var i = 0; i < relic.SubPropertyList.Count; i++)
                {
                    var subStat = relic.SubPropertyList[i];
                    var subStatImage = m_StatImages[subStat.PropertyType!.Value];
                    var xOffset = i % 2 * 250;
                    var yOffset = i / 2 * 60;
                    ctx.DrawImage(subStatImage, new Point(250 + xOffset, 26 + yOffset), 1f);
                    ctx.DrawText(subStat.Value, font, Color.White, new PointF(314 + xOffset, 30 + yOffset));
                }

                relicImage.Dispose();
            });

            return template;
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Failed to create relic slot image for {RelicId}", relic.Id);
            throw;
        }
    }

    private Color GetBackgroundColor(string element)
    {
        m_Logger.LogTrace("Getting background color for element: {Element}", element);
        var color = element switch
        {
            "Pyro" => Color.ParseHex("#BF8667"),
            "Hydro" => Color.ParseHex("#7A92FF"),
            "Electro" => Color.ParseHex("#9E65C8"),
            "Dendro" => Color.ParseHex("#529D62"),
            "Cryo" => Color.ParseHex("#78CACC"),
            "Geo" => Color.ParseHex("#B5A155"),
            "Anemo" => Color.ParseHex("7fB29E"),
            _ => Color.White
        };

        if (element == "_")
            m_Logger.LogWarning("Unknown element type: {Element}, using default color", element);

        return color;
    }
}
