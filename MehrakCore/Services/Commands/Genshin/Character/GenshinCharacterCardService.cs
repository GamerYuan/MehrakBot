#region

using System.Numerics;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Models;
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

namespace MehrakCore.Services.Commands.Genshin.Character;

public class GenshinCharacterCardService : ICharacterCardService<GenshinCharacterInformation>
{
    private readonly ImageRepository m_ImageRepository;
    private readonly ILogger<GenshinCharacterCardService> m_Logger;

    private readonly Dictionary<int, Image> m_StatImages;

    private const string BasePath = "genshin_{0}";
    private const string StatsPath = "genshin_stats_{0}";

    private readonly Font m_SmallFont;
    private readonly Font m_MediumFont;
    private readonly Font m_NormalFont;
    private readonly Font m_TitleFont;

    private readonly JpegEncoder m_JpegEncoder;
    private readonly Image<Rgba32> m_RelicSlotTemplate;

    public GenshinCharacterCardService(ImageRepository imageRepository, ILogger<GenshinCharacterCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_Logger = logger;

        var collection = new FontCollection();
        var fontFamily = collection.Add("Assets/Fonts/genshin.ttf");

        m_TitleFont = fontFamily.CreateFont(64);
        m_NormalFont = fontFamily.CreateFont(40);
        m_MediumFont = fontFamily.CreateFont(32);
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
                var image = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(path));
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

        m_RelicSlotTemplate = new Image<Rgba32>(970, 170);
        m_RelicSlotTemplate.Mutate(ctx =>
        {
            ctx.Fill(new Rgba32(0, 0, 0, 0.25f));
            ctx.ApplyRoundedCorners(15);
        });

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
            // Prepare all image loading tasks
            var overlayTask = Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("genshin_bg"));
            var background = new Image<Rgba32>(3240, 1080);
            disposableResources.Add(background);

            var characterPortraitTask =
                Image.LoadAsync<Rgba32>(
                    await m_ImageRepository.DownloadFileToStreamAsync(string.Format(BasePath, charInfo.Base.Id)));

            var weaponImageTask =
                Image.LoadAsync<Rgba32>(
                    await m_ImageRepository.DownloadFileToStreamAsync(string.Format(BasePath,
                        charInfo.Base.Weapon.Id)));

            var constellationTasks = charInfo.Constellations.Select(async x =>
            {
                var image = await Image.LoadAsync(
                    await m_ImageRepository.DownloadFileToStreamAsync(string.Format(BasePath, x.Id)));
                return (Active: x.IsActived.GetValueOrDefault(false), Image: image);
            }).Reverse().ToArray();

            var skillTasks = charInfo.Skills
                .Where(x => x.SkillType!.Value == 1 && !x.Desc.Contains("Alternate Sprint"))
                .Select(async x =>
                {
                    var image = await Image.LoadAsync(
                        await m_ImageRepository.DownloadFileToStreamAsync(string.Format(BasePath, x.SkillId)));
                    return (Data: x, Image: image);
                }).Reverse().ToArray();

            // Relic image tasks
            var relicImageTasks = Enumerable.Range(0, 5).Select(async i =>
            {
                var relic = charInfo.Relics.FirstOrDefault(x => x.Pos == i + 1);
                if (relic != null)
                {
                    var relicImage = await CreateRelicSlotImageAsync(relic);
                    return relicImage;
                }
                else
                {
                    var templateRelicImage = await CreateTemplateRelicSlotImageAsync(i + 1);
                    return templateRelicImage;
                }
            }).ToArray();

            // Await all image loading tasks concurrently
            await Task.WhenAll(
                overlayTask,
                characterPortraitTask,
                weaponImageTask,
                Task.WhenAll(constellationTasks),
                Task.WhenAll(skillTasks),
                Task.WhenAll(relicImageTasks)
            );

            // Add loaded images to disposable resources
            var overlay = overlayTask.Result;
            disposableResources.Add(overlay);

            var characterPortrait = characterPortraitTask.Result;
            disposableResources.Add(characterPortrait);

            var weaponImage = weaponImageTask.Result;
            disposableResources.Add(weaponImage);

            var constellationIcons = (await Task.WhenAll(constellationTasks)).ToArray();
            disposableResources.AddRange(constellationIcons.Select(c => c.Image));

            var skillIcons = (await Task.WhenAll(skillTasks)).ToArray();
            disposableResources.AddRange(skillIcons.Select(s => s.Image));

            var relics = (await Task.WhenAll(relicImageTasks)).ToArray();
            disposableResources.AddRange(relics);

            m_Logger.LogDebug("Processing {Count} relic images", charInfo.Relics.Count);
            Dictionary<RelicSet, int> relicActivation = new();
            for (int i = 0; i < 5; i++)
            {
                var relic = charInfo.Relics.FirstOrDefault(x => x.Pos == i + 1);
                if (relic == null) continue;
                if (!relicActivation.TryAdd(relic.RelicSet, 1))
                    relicActivation[relic.RelicSet]++;
            }

            Dictionary<string, int> activeSet = new();
            foreach (var relicSet in relicActivation)
                if (relicSet.Key.Affixes.Any(x => relicSet.Value >= x.ActivationNumber.GetValueOrDefault(0)))
                    activeSet.Add(relicSet.Key.Name, relicSet.Value);

            m_Logger.LogDebug("Found {Count} active relic sets", activeSet.Count);

            // Display based on the order
            // 1. Base stats
            // 2. EM
            // 3. Other stats
            var bonusStats = charInfo.SelectedProperties.OrderBy(x => x.PropertyType)
                .Where(x => float.Parse(x.Final.TrimEnd('%')) >
                            StatMappingUtility.GetDefaultValue(x.PropertyType!.Value, GameName.Genshin)).ToArray();

            StatProperty[] stats;
            if (bonusStats.Length >= 6)
                stats = charInfo.BaseProperties.Take(4)
                    .Where(x => float.Parse(x.Final.TrimEnd('%')) >
                                StatMappingUtility.GetDefaultValue(x.PropertyType!.Value, GameName.Genshin))
                    .Concat(bonusStats)
                    .DistinctBy(x => x.PropertyType)
                    .ToArray();
            else
                stats = charInfo.BaseProperties.Take(4)
                    .Concat(charInfo.SelectedProperties.OrderBy(x => x.PropertyType))
                    .Take(7).ToArray();

            m_Logger.LogTrace("Compositing character card image");

            background.Mutate(ctx =>
            {
                var backgroundColor = GetBackgroundColor(charInfo.Base.Element ?? "None");
                ctx.Fill(backgroundColor);
                ctx.DrawImage(overlay, PixelColorBlendingMode.Overlay, 1f);

                var textColor = Color.White;

                ctx.DrawImage(characterPortrait, new Point(-50, 50 + (1080 - characterPortrait.Height) / 2), 1f);

                ctx.DrawText(charInfo.Base.Name, m_TitleFont, Color.Black, new PointF(73, 58));
                ctx.DrawText(charInfo.Base.Name, m_TitleFont, textColor, new PointF(70, 55));
                ctx.DrawText($"Lv. {charInfo.Base.Level}", m_NormalFont, Color.Black, new PointF(73, 138));
                ctx.DrawText($"Lv. {charInfo.Base.Level}", m_NormalFont, textColor, new PointF(70, 135));

                for (int i = 0; i < skillIcons.Length; i++)
                {
                    var skill = skillIcons[i];
                    var offset = i * 150;
                    var skillEllipse = new EllipsePolygon(120, 920 - offset, 60);
                    ctx.Fill(Color.DarkSlateGray, skillEllipse);
                    ctx.DrawImage(skill.Image, new Point(70, 870 - offset), 1f);
                    ctx.Draw(backgroundColor, 5f, skillEllipse.AsClosedPath());
                    var talentEllipse = new EllipsePolygon(120, 980 - offset, 25);
                    ctx.Fill(Color.DarkGray, talentEllipse);
                    ctx.DrawText(new RichTextOptions(m_MediumFont)
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                        Origin = new Vector2(120, 983 - offset)
                    }, skill.Data.Level.ToString()!, textColor);
                }

                ctx.DrawText(gameUid, m_SmallFont, textColor, new PointF(60, 1040));

                for (int i = 0; i < constellationIcons.Length; i++)
                {
                    var constellation = constellationIcons[i];
                    if (!constellation.Active)
                        constellation.Image.Mutate(x => x.Brightness(0.5f));
                    var offset = i * 140;
                    var constEllipse = new EllipsePolygon(1050, 1000 - offset, 50);
                    ctx.Fill(Color.DarkSlateGray, constEllipse);
                    ctx.DrawImage(constellation.Image, new Point(1005, 955 - offset), 1f);
                    ctx.Draw(backgroundColor, 5f, constEllipse.AsClosedPath());
                }

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
                var statSize =
                    TextMeasurer.MeasureSize(charInfo.Weapon.MainProperty.Final, new TextOptions(m_NormalFont));
                var statBackground = new Image<Rgba32>(80 + (int)statSize.Width, 60);
                statBackground.Mutate(x =>
                {
                    x.Fill(new Rgba32(0, 0, 0, 0.45f));
                    x.ApplyRoundedCorners(10);
                });
                ctx.DrawImage(statBackground, new Point(1450, 230), 1f);
                ctx.DrawImage(m_StatImages[charInfo.Weapon.MainProperty.PropertyType!.Value], new Point(1455, 236),
                    1f);
                ctx.DrawText(charInfo.Weapon.MainProperty.Final, m_NormalFont, textColor, new PointF(1514, 240));
                if (charInfo.Weapon.SubProperty != null)
                {
                    var substatSize =
                        TextMeasurer.MeasureSize(charInfo.Weapon.SubProperty.Final, new TextOptions(m_NormalFont));
                    var substatBackground = new Image<Rgba32>(80 + (int)substatSize.Width, 60);
                    substatBackground.Mutate(x =>
                    {
                        x.Fill(new Rgba32(0, 0, 0, 0.45f));
                        x.ApplyRoundedCorners(10);
                    });
                    ctx.DrawImage(substatBackground, new Point(1630, 230), 1f);
                    ctx.DrawImage(m_StatImages[charInfo.Weapon.SubProperty.PropertyType!.Value], new Point(1635, 236),
                        1f);
                    ctx.DrawText(charInfo.Weapon.SubProperty.Final, m_NormalFont, textColor, new PointF(1694, 240));
                }

                var spacing = 700 / stats.Length;

                for (int i = 0; i < stats.Length; i++)
                {
                    var stat = stats[i];
                    var y = 360 + spacing * i;
                    ctx.DrawImage(m_StatImages[stat.PropertyType!.Value], new Point(1200, y - 4), 1f);
                    ctx.DrawText(StatMappingUtility.GenshinMapping[stat.PropertyType!.Value], m_NormalFont, textColor,
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
                        Origin = new Vector2(2750, 1020),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        TextAlignment = TextAlignment.End,
                        LineSpacing = 1.5f,
                        WrappingLength = 500
                    }, relicSetText, Color.White);

                    ctx.DrawText(new RichTextOptions(m_SmallFont)
                    {
                        Origin = new Vector2(2800, 1020),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        LineSpacing = 1.5f
                    }, relicSetValueText, Color.White);
                }
                else
                {
                    ctx.DrawText(new RichTextOptions(m_SmallFont)
                    {
                        Origin = new Vector2(2725, 1020),
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
            throw new CommandException("An error occurred while generating the character card", ex);
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

            var relicImage = await Image.LoadAsync<Rgba32>(
                await m_ImageRepository.DownloadFileToStreamAsync(path));

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
                    var xOffset = i % 2 * 290;
                    var yOffset = i / 2 * 80;
                    var color = Color.White;
                    if (subStat.PropertyType is 2 or 5 or 8)
                    {
                        var dim = subStatImage.CloneAs<Rgba32>();
                        dim.Mutate(x => x.Brightness(0.5f));
                        ctx.DrawImage(dim, new Point(375 + xOffset, 26 + yOffset), 1f);
                        color = Color.FromRgb(128, 128, 128);
                    }
                    else
                    {
                        ctx.DrawImage(subStatImage, new Point(375 + xOffset, 26 + yOffset), 1f);
                    }

                    ctx.DrawText(subStat.Value, m_NormalFont, color, new PointF(439 + xOffset, 30 + yOffset));

                    var rolls = string.Concat(Enumerable.Repeat('.', subStat.Times.GetValueOrDefault(0) + 1));
                    ctx.DrawText(rolls, m_NormalFont, color, new PointF(575 + xOffset, 15 + yOffset));
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
            var path = $"genshin_relic_template_{position}";
            m_Logger.LogDebug("Loading template relic image from {Path}", path);

            var relicImage = await Image.LoadAsync<Rgba32>(
                await m_ImageRepository.DownloadFileToStreamAsync(path));
            relicImage.Mutate(x => x.Resize(new Size(0, 150), KnownResamplers.Bicubic, true));
            var template = CreateRelicSlot();
            template.Mutate(ctx =>
            {
                ctx.DrawImage(relicImage, new Point(25, 5), 1f);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(525, 95),
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
        return m_RelicSlotTemplate.Clone();
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
