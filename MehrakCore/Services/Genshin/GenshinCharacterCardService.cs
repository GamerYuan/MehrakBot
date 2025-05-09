#region

using System.Numerics;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Repositories;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ImageExtensions = MehrakCore.Utility.ImageExtensions;

#endregion

namespace MehrakCore.Services.Genshin;

public class GenshinCharacterCardService : ICharacterCardService<GenshinCharacterInformation>
{
    private readonly ImageRepository m_ImageRepository;
    private readonly ILogger<GenshinCharacterCardService> m_Logger;

    private readonly Dictionary<int, Image> m_StatImages;

    private const string BasePath = "genshin_{0}";
    private const string StatsPath = "genshin_stats_{0}.png";

    private readonly Font m_SmallFont;
    private readonly Font m_NormalFont;
    private readonly Font m_TitleFont;

    private readonly JpegEncoder m_JpegEncoder;

    public GenshinCharacterCardService(ImageRepository imageRepository, ILogger<GenshinCharacterCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_Logger = logger;

        var collection = new FontCollection();
        var fontFamily = collection.Add("Fonts/zh-cn.ttf");

        m_TitleFont = fontFamily.CreateFont(64);
        m_NormalFont = fontFamily.CreateFont(40);
        m_SmallFont = fontFamily.CreateFont(28);

        m_JpegEncoder = new JpegEncoder
        {
            Quality = 90,
            Interleaved = false
        };

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

    public async Task<Stream> GenerateCharacterCardAsync(GenshinCharacterInformation charInfo, string gameUid)
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
                await Task.WhenAll(charInfo.Constellations.Select(async x =>
                {
                    var image = Image.Load(
                        await m_ImageRepository.DownloadFileAsBytesAsync(string.Format(BasePath, x.Id)));
                    disposableResources.Add(image);
                    return (Active: x.IsActived.GetValueOrDefault(false), Image: image);
                }).Reverse());

            m_Logger.LogDebug("Loading {Count} skill icons", charInfo.Skills.Count);
            var skillIcons = await Task.WhenAll(charInfo.Skills
                .Where(x => x.SkillType!.Value == 1 && !x.Desc.Contains("Alternate Sprint"))
                .Select(async x =>
                {
                    var image = Image.Load(
                        await m_ImageRepository.DownloadFileAsBytesAsync(string.Format(BasePath, x.SkillId)));
                    disposableResources.Add(image);
                    return (Data: x, Image: image);
                }).Reverse());

            m_Logger.LogDebug("Processing {Count} relic images", charInfo.Relics.Count);
            Dictionary<RelicSet, int> relicActivation = new();
            var relics = new Image<Rgba32>[5];
            for (int i = 0; i < 5; i++)
            {
                var relic = charInfo.Relics.FirstOrDefault(x => x.Pos == i + 1);
                if (relic != null)
                {
                    var relicImage = await CreateRelicSlotImageAsync(relic);
                    disposableResources.Add(relicImage);
                    relics[i] = relicImage;
                    if (!relicActivation.TryAdd(relic.RelicSet, 1)) relicActivation[relic.RelicSet]++;
                }
                else
                {
                    var templateRelicImage = await CreateTemplateRelicSlotImageAsync(i + 1);
                    disposableResources.Add(templateRelicImage);
                    relics[i] = templateRelicImage;
                }
            }

            Dictionary<string, int> activeSet = new();
            foreach (var relicSet in relicActivation)
                if (relicSet.Key.Affixes.Any(x => relicSet.Value >= x.ActivationNumber.GetValueOrDefault(0)))
                    activeSet.Add(relicSet.Key.Name, relicSet.Value);

            m_Logger.LogDebug("Found {Count} active relic sets", activeSet.Count);

            m_Logger.LogTrace("Compositing character card image");

            background.Mutate(ctx =>
            {
                var backgroundColor = GetBackgroundColor(charInfo.Base.Element);
                ctx.Fill(backgroundColor);
                ctx.DrawImage(overlay, PixelColorBlendingMode.Overlay, 1f);

                var textColor = Color.White;

                ctx.DrawImage(characterPortrait, new Point(-50, (1080 - characterPortrait.Height) / 2), 1f);

                ctx.DrawText(charInfo.Base.Name, m_TitleFont, textColor, new PointF(50, 80));
                ctx.DrawText($"Lv. {charInfo.Base.Level}", m_NormalFont, textColor, new PointF(50, 160));

                for (int i = 0; i < skillIcons.Length; i++)
                {
                    var skill = skillIcons[i];
                    skill.Image.Mutate(x => x.Resize(new Size(120, 0), KnownResamplers.Bicubic, true));
                    var offset = i * 200;
                    var skillEllipse = new EllipsePolygon(100, 920 - offset, 70);
                    ctx.Fill(Color.DarkSlateGray, skillEllipse).Draw(backgroundColor, 5f, skillEllipse.AsClosedPath());
                    ctx.DrawImage(skill.Image, new Point(40, 860 - offset), 1f);
                    var talentEllipse = new EllipsePolygon(100, 990 - offset, 30);
                    ctx.Fill(Color.DarkGray, talentEllipse);
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Origin = new Vector2(100, 970 - offset)
                    }, skill.Data.Level.ToString()!, textColor);
                }

                ctx.DrawText(gameUid, m_SmallFont, textColor, new PointF(40, 1040));

                for (int i = 0; i < constellationIcons.Length; i++)
                {
                    var constellation = constellationIcons[i];
                    constellation.Image.Mutate(x => x.Resize(new Size(90, 0), KnownResamplers.Bicubic, true));
                    if (!constellation.Active)
                        constellation.Image.Mutate(x => x.Brightness(0.5f));
                    var offset = i * 140;
                    var constEllipse = new EllipsePolygon(1050, 1000 - offset, 50);
                    ctx.Fill(Color.DarkSlateGray, constEllipse).Draw(backgroundColor, 5f, constEllipse.AsClosedPath());
                    ctx.DrawImage(constellation.Image, new Point(1005, 955 - offset), 1f);
                }

                weaponImage.Mutate(x => x.Resize(new Size(200, 0), KnownResamplers.Bicubic, true));
                ctx.DrawImage(weaponImage, new Point(1200, 40), 1f);
                ctx.DrawImage(ImageExtensions.GenerateStarRating(charInfo.Weapon.Rarity.GetValueOrDefault(1)),
                    new Point(1220, 240), 1f);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(1450, 120),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    WrappingLength = 650
                }, charInfo.Weapon.Name, textColor);
                ctx.DrawText('R' + charInfo.Weapon.AffixLevel!.Value.ToString(), m_NormalFont, textColor,
                    new PointF(1450, 160));
                ctx.DrawText($"Lv. {charInfo.Weapon.Level}", m_NormalFont, textColor, new PointF(1550, 160));
                ctx.DrawImage(m_StatImages[charInfo.Weapon.MainProperty.PropertyType!.Value], new Point(1450, 236),
                    1f);
                ctx.DrawText(charInfo.Weapon.MainProperty.Final, m_NormalFont, textColor, new PointF(1514, 240));
                if (charInfo.Weapon.SubProperty != null)
                {
                    ctx.DrawImage(m_StatImages[charInfo.Weapon.SubProperty.PropertyType!.Value], new Point(1650, 236),
                        1f);
                    ctx.DrawText(charInfo.Weapon.SubProperty.Final, m_NormalFont, textColor, new PointF(1714, 240));
                }

                // Display based on the order
                // 1. Base stats
                // 2. EM
                // 3. Other stats
                var bonusStats = charInfo.SelectedProperties.OrderBy(x => x.PropertyType)
                    .Where(x => float.Parse(x.Final.TrimEnd('%')) >
                                StatMappingUtility.GetDefaultValue(x.PropertyType!.Value)).ToArray();

                StatProperty[] stats;
                if (bonusStats.Length >= 6)
                    stats = charInfo.BaseProperties.Take(4)
                        .Where(x => float.Parse(x.Final.TrimEnd('%')) >
                                    StatMappingUtility.GetDefaultValue(x.PropertyType!.Value)).Concat(bonusStats)
                        .DistinctBy(x => x.PropertyType)
                        .ToArray();
                else
                    stats = charInfo.BaseProperties.Take(4)
                        .Concat(charInfo.SelectedProperties.OrderBy(x => x.PropertyType))
                        .Take(7).ToArray();

                var spacing = 700 / stats.Length;

                for (int i = 0; i < stats.Length; i++)
                {
                    var stat = stats[i];
                    var y = 360 + spacing * i;
                    ctx.DrawImage(m_StatImages[stat.PropertyType!.Value], new Point(1200, y - 4), 1f);
                    ctx.DrawText(StatMappingUtility.Mapping[stat.PropertyType!.Value], m_NormalFont, textColor,
                        new PointF(1264, y));
                    if (StatMappingUtility.IsBaseStat(stat.PropertyType!.Value))
                    {
                        ctx.DrawText(new RichTextOptions(m_NormalFont)
                        {
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Origin = new Vector2(2100, y - 15)
                        }, stat.Final, textColor);
                        int xPos = 2100;
                        if (int.Parse(stat.Final.TrimEnd('%')) > int.Parse(stat.Base.TrimEnd('%')))
                        {
                            var bonusText = $"\u00A0+{stat.Add}";
                            xPos -= (int)TextMeasurer.MeasureSize(bonusText, new TextOptions(m_SmallFont)).Width;
                            ctx.DrawText(new RichTextOptions(m_SmallFont)
                            {
                                HorizontalAlignment = HorizontalAlignment.Right,
                                Origin = new Vector2(2100, y + 25)
                            }, bonusText, Color.LightGreen);
                        }

                        ctx.DrawText(new RichTextOptions(m_SmallFont)
                            {
                                HorizontalAlignment = HorizontalAlignment.Right,
                                Origin = new Vector2(xPos, y + 25)
                            }, $"{stat.Base}", Color.LightGray);
                    }
                    else
                    {
                        ctx.DrawText(new RichTextOptions(m_NormalFont)
                        {
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Origin = new Vector2(2100, y)
                        }, stat.Final, textColor);
                    }
                }

                for (int i = 0; i < relics.Length; i++)
                {
                    var relic = relics[i];
                    ctx.DrawImage(relic, new Point(2200, 40 + i * 185), 1f);
                }

                if (activeSet.Count > 0)
                {
                    var relicSetText = string.Join('\n', activeSet.Keys);
                    var relicSetValueText = string.Join('\n', activeSet.Values);

                    ctx.DrawText(new RichTextOptions(m_SmallFont)
                    {
                        Origin = new Vector2(2775, 1020),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        TextAlignment = TextAlignment.End,
                        LineSpacing = 1.5f
                    }, relicSetText, Color.White);

                    ctx.DrawText(new RichTextOptions(m_SmallFont)
                    {
                        Origin = new Vector2(2825, 1020),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        LineSpacing = 1.5f
                    }, relicSetValueText, Color.White);
                }
                else
                {
                    ctx.DrawText(new RichTextOptions(m_SmallFont)
                    {
                        Origin = new Vector2(2750, 1020),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }, "No active set", Color.White);
                }
            });

            m_Logger.LogDebug("Saving character card to stream");
            var stream = new MemoryStream();
            await background.SaveAsJpegAsync(stream, m_JpegEncoder);
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

    private async Task<Image<Rgba32>> CreateRelicSlotImageAsync(Relic relic)
    {
        m_Logger.LogTrace("Creating relic slot image for {RelicId}", relic.Id);
        try
        {
            var path = string.Format(BasePath, relic.Id);
            m_Logger.LogTrace("Loading relic image from {Path}", path);

            var relicImage = Image.Load<Rgba32>(
                await m_ImageRepository.DownloadFileAsBytesAsync(path));

            var template = CreateRelicSlot();
            template.Mutate(ctx =>
            {
                ctx.DrawImage(relicImage, new Point(-40, -40), 1f);
                ctx.DrawImage(m_StatImages[relic.MainProperty.PropertyType!.Value], new Point(280, 20), 1f);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    TextAlignment = TextAlignment.End,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Origin = new Vector2(320, 70)
                }, relic.MainProperty.Value, Color.White);
                ctx.DrawText(new RichTextOptions(m_SmallFont)
                    {
                        TextAlignment = TextAlignment.End,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Origin = new Vector2(320, 130)
                    }, $"+{relic.Level!.Value}", Color.White);
                var stars = ImageExtensions.GenerateStarRating(relic.Rarity.GetValueOrDefault(1));
                stars.Mutate(x => x.Resize(0, 25));
                ctx.DrawImage(stars, new Point(120, 130), 1f);

                for (var i = 0; i < relic.SubPropertyList.Count; i++)
                {
                    var subStat = relic.SubPropertyList[i];
                    var subStatImage = m_StatImages[subStat.PropertyType!.Value];
                    var xOffset = i % 2 * 250;
                    var yOffset = i / 2 * 80;
                    ctx.DrawImage(subStatImage, new Point(450 + xOffset, 26 + yOffset), 1f);
                    ctx.DrawText(subStat.Value, m_NormalFont, Color.White, new PointF(514 + xOffset, 30 + yOffset));
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

    private async Task<Image<Rgba32>> CreateTemplateRelicSlotImageAsync(int position)
    {
        m_Logger.LogTrace("Creating template relic slot image for position {Position}", position);
        try
        {
            var path = $"genshin_relic_template_{position}.png";
            m_Logger.LogDebug("Loading template relic image from {Path}", path);

            var relicImage = Image.Load<Rgba32>(
                await m_ImageRepository.DownloadFileAsBytesAsync(path));
            relicImage.Mutate(x => x.Resize(new Size(0, 150), KnownResamplers.Bicubic, true));
            var template = CreateRelicSlot();
            template.Mutate(ctx =>
            {
                ctx.DrawImage(relicImage, new Point(25, 5), 1f);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(550, 95),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                }, "No Artifact", Color.White);
            });

            return template;
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Failed to create template relic slot image for position {Position}", position);
            throw;
        }
    }

    private Image<Rgba32> CreateRelicSlot()
    {
        var template = new Image<Rgba32>(1000, 170);
        template.Mutate(ctx =>
        {
            ctx.Fill(new Rgba32(0, 0, 0, 0.25f));
            ctx.ApplyRoundedCorners(15);
        });

        return template;
    }

    private Color GetBackgroundColor(string element)
    {
        m_Logger.LogTrace("Getting background color for element: {Element}", element);
        var color = element switch
        {
            "Pyro" => Color.ParseHex("#8F321A"),
            "Hydro" => Color.ParseHex("#2059B9"),
            "Electro" => Color.ParseHex("#7D38B3"),
            "Dendro" => Color.ParseHex("#006D20"),
            "Cryo" => Color.ParseHex("#008C8E"),
            "Geo" => Color.ParseHex("#806A00"),
            "Anemo" => Color.ParseHex("137B52"),
            _ => Color.SlateGray
        };

        if (element == "_")
            m_Logger.LogWarning("Unknown element type: {Element}, using default color", element);

        return color;
    }
}
