#region

using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Constants;
using MehrakCore.Models;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Numerics;

#endregion

namespace MehrakCore.Services.Commands.Genshin.Character;

public class GenshinCharacterCardService : ICharacterCardService<GenshinCharacterInformation>
{
    private readonly ImageRepository m_ImageRepository;
    private readonly ILogger<GenshinCharacterCardService> m_Logger;

    private Dictionary<int, Image> m_StatImages = null!;

    private const string BasePath = FileNameFormat.GenshinFileName;
    private const string StatsPath = FileNameFormat.GenshinStatsName;

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

        FontCollection collection = new();
        FontFamily fontFamily = collection.Add("Assets/Fonts/genshin.ttf");

        m_TitleFont = fontFamily.CreateFont(64);
        m_NormalFont = fontFamily.CreateFont(40);
        m_MediumFont = fontFamily.CreateFont(32);
        m_SmallFont = fontFamily.CreateFont(28);

        m_JpegEncoder = new JpegEncoder
        {
            Quality = 90,
            Interleaved = false
        };

        m_RelicSlotTemplate = new Image<Rgba32>(970, 170);
        m_RelicSlotTemplate.Mutate(ctx =>
        {
            ctx.Fill(new Rgba32(0, 0, 0, 0.25f));
            ctx.ApplyRoundedCorners(15);
        });
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        int[] statIds =
            [1, 2, 3, 4, 5, 6, 7, 8, 9, 20, 22, 23, 26, 27, 28, 30, 40, 41, 42, 43, 44, 45, 46, 2000, 2001, 2002];

        m_Logger.LogDebug("Loading {Count} stat icons", statIds.Length);
        m_StatImages = await statIds.ToAsyncEnumerable().SelectAwait(async x =>
        {
            try
            {
                string path = string.Format(StatsPath, x);
                m_Logger.LogTrace("Downloading stat icon {StatId}: {Path}", x, path);
                Image image = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(path));
                image.Mutate(ctx => ctx.Resize(new Size(48, 0), KnownResamplers.Bicubic, true));
                return new KeyValuePair<int, Image>(x, image);
            }
            catch (Exception ex)
            {
                m_Logger.LogError(ex, "Failed to load stat icon {StatId}", x);
                throw;
            }
        }).ToDictionaryAsync(kvp => kvp.Key, kvp => kvp.Value, cancellationToken: cancellationToken);

        m_Logger.LogInformation(
            "Resources initialized successfully with {Count} icons.",
            m_StatImages.Count);

        m_Logger.LogInformation("GenshinCharacterCardService initialized");
    }

    public async Task<Stream> GenerateCharacterCardAsync(GenshinCharacterInformation charInfo, string gameUid)
    {
        m_Logger.LogInformation("Generating character card for {CharacterName} (ID: {CharacterId})",
            charInfo.Base.Name, charInfo.Base.Id);

        List<IDisposable> disposableResources = [];

        try
        {
            // Prepare all image loading tasks
            Task<Image> overlayTask = Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("genshin_bg"));
            Image<Rgba32> background = new(3240, 1080);
            disposableResources.Add(background);

            Task<Image<Rgba32>> characterPortraitTask =
                Image.LoadAsync<Rgba32>(
                    await m_ImageRepository.DownloadFileToStreamAsync(string.Format(BasePath, charInfo.Base.Id)));

            Task<Image<Rgba32>> weaponImageTask =
                Image.LoadAsync<Rgba32>(
                    await m_ImageRepository.DownloadFileToStreamAsync(string.Format(BasePath,
                        charInfo.Base.Weapon.Id)));

            Task<(bool Active, Image Image)>[] constellationTasks = [.. charInfo.Constellations.Select(async x =>
            {
                Image image = await Image.LoadAsync(
                    await m_ImageRepository.DownloadFileToStreamAsync(string.Format(BasePath, x.Id)));
                return (Active: x.IsActived.GetValueOrDefault(false), Image: image);
            }).Reverse()];

            Task<(Skill Data, Image Image)>[] skillTasks = [.. charInfo.Skills
                .Where(x => x.SkillType!.Value == 1 && !x.Desc.Contains("Alternate Sprint"))
                .Select(async x =>
                {
                    Image image = await Image.LoadAsync(
                        await m_ImageRepository.DownloadFileToStreamAsync(
                            string.Format(FileNameFormat.GenshinSkillName, charInfo.Base.Id, x.SkillId)));
                    return (Data: x, Image: image);
                }).Reverse()];

            // Relic image tasks
            Task<Image<Rgba32>>[] relicImageTasks = [.. Enumerable.Range(0, 5).Select(async i =>
            {
                Relic? relic = charInfo.Relics.FirstOrDefault(x => x.Pos == i + 1);
                if (relic != null)
                {
                    Image<Rgba32> relicImage = await CreateRelicSlotImageAsync(relic);
                    return relicImage;
                }
                else
                {
                    Image<Rgba32> templateRelicImage = await CreateTemplateRelicSlotImageAsync(i + 1);
                    return templateRelicImage;
                }
            })];

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
            Image overlay = overlayTask.Result;
            disposableResources.Add(overlay);

            Image<Rgba32> characterPortrait = characterPortraitTask.Result;
            disposableResources.Add(characterPortrait);

            Image<Rgba32> weaponImage = weaponImageTask.Result;
            disposableResources.Add(weaponImage);

            (bool Active, Image Image)[] constellationIcons = [.. (await Task.WhenAll(constellationTasks))];
            disposableResources.AddRange(constellationIcons.Select(c => c.Image));

            (Skill Data, Image Image)[] skillIcons = [.. (await Task.WhenAll(skillTasks))];
            disposableResources.AddRange(skillIcons.Select(s => s.Image));

            Image<Rgba32>[] relics = [.. (await Task.WhenAll(relicImageTasks))];
            disposableResources.AddRange(relics);

            m_Logger.LogDebug("Processing {Count} relic images", charInfo.Relics.Count);
            Dictionary<RelicSet, int> relicActivation = [];
            for (int i = 0; i < 5; i++)
            {
                Relic? relic = charInfo.Relics.FirstOrDefault(x => x.Pos == i + 1);
                if (relic == null) continue;
                if (!relicActivation.TryAdd(relic.RelicSet, 1))
                    relicActivation[relic.RelicSet]++;
            }

            Dictionary<string, int> activeSet = [];
            foreach (KeyValuePair<RelicSet, int> relicSet in
                relicActivation.Where(relicSet => relicSet.Key.Affixes.Any(x => relicSet.Value >= x.ActivationNumber.GetValueOrDefault(0))))
            {
                activeSet.Add(relicSet.Key.Name, relicSet.Value);
            }

            m_Logger.LogDebug("Found {Count} active relic sets", activeSet.Count);

            // Display based on the order
            // 1. Base stats
            // 2. EM
            // 3. Other stats
            StatProperty[] bonusStats = [.. charInfo.SelectedProperties.Where(x => float.Parse(x.Final.TrimEnd('%')) >
                            StatMappingUtility.GetDefaultValue(x.PropertyType!.Value, GameName.Genshin))
                .OrderBy(x => x.PropertyType)];

            StatProperty[] stats;
            if (bonusStats.Length >= 6)
                stats = [.. charInfo.BaseProperties.Take(4)
                    .Where(x => float.Parse(x.Final.TrimEnd('%')) >
                                StatMappingUtility.GetDefaultValue(x.PropertyType!.Value, GameName.Genshin))
                    .Concat(bonusStats)
                    .DistinctBy(x => x.PropertyType)];
            else
                stats = [.. charInfo.BaseProperties.Take(4)
                    .Concat(charInfo.SelectedProperties.OrderBy(x => x.PropertyType))
                    .Take(7)];

            m_Logger.LogTrace("Compositing character card image");

            background.Mutate(ctx =>
            {
                Color backgroundColor = GetBackgroundColor(charInfo.Base.Element ?? "None");
                ctx.Fill(backgroundColor);
                ctx.DrawImage(overlay, PixelColorBlendingMode.Overlay, 1f);

                Color textColor = Color.White;

                ctx.DrawImage(characterPortrait,
                    new Point((1280 - characterPortrait.Width) / 2, 100 + (1080 - characterPortrait.Height) / 2),
                    1f);

                ctx.DrawText(charInfo.Base.Name, m_TitleFont, Color.Black, new PointF(73, 58));
                ctx.DrawText(charInfo.Base.Name, m_TitleFont, textColor, new PointF(70, 55));
                ctx.DrawText($"Lv. {charInfo.Base.Level}", m_NormalFont, Color.Black, new PointF(73, 138));
                ctx.DrawText($"Lv. {charInfo.Base.Level}", m_NormalFont, textColor, new PointF(70, 135));

                for (int i = 0; i < skillIcons.Length; i++)
                {
                    (Skill Data, Image Image) skill = skillIcons[i];
                    int offset = i * 150;
                    EllipsePolygon skillEllipse = new(120, 920 - offset, 60);
                    ctx.Fill(Color.DarkSlateGray, skillEllipse);
                    ctx.DrawImage(skill.Image, new Point(70, 870 - offset), 1f);
                    ctx.Draw(backgroundColor, 5f, skillEllipse.AsClosedPath());
                    EllipsePolygon talentEllipse = new(120, 980 - offset, 25);
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
                    (bool Active, Image Image) constellation = constellationIcons[i];
                    if (!constellation.Active)
                        constellation.Image.Mutate(x => x.Brightness(0.5f));
                    int offset = i * 140;
                    EllipsePolygon constEllipse = new(1050, 1000 - offset, 50);
                    ctx.Fill(Color.DarkSlateGray, constEllipse);
                    ctx.DrawImage(constellation.Image, new Point(1005, 955 - offset), 1f);
                    ctx.Draw(backgroundColor, 5f, constEllipse.AsClosedPath());
                }

                ctx.DrawImage(weaponImage, new Point(1200, 40), 1f);
                ctx.DrawImage(ImageUtility.GenerateStarRating(charInfo.Weapon.Rarity.GetValueOrDefault(1)),
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
                FontRectangle statSize =
                    TextMeasurer.MeasureSize(charInfo.Weapon.MainProperty.Final, new TextOptions(m_NormalFont));
                Image<Rgba32> statBackground = new(80 + (int)statSize.Width, 60);
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
                    FontRectangle substatSize =
                        TextMeasurer.MeasureSize(charInfo.Weapon.SubProperty.Final, new TextOptions(m_NormalFont));
                    Image<Rgba32> substatBackground = new(80 + (int)substatSize.Width, 60);
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

                int spacing = 700 / stats.Length;

                for (int i = 0; i < stats.Length; i++)
                {
                    StatProperty stat = stats[i];
                    int y = 360 + spacing * i;
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
                            string bonusText = $"\u00A0+{stat.Add}";
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
                    Image<Rgba32> relic = relics[i];
                    ctx.DrawImage(relic, new Point(2200, 40 + i * 185), 1f);
                }

                if (activeSet.Count > 0)
                {
                    string relicSetText = string.Join('\n', activeSet.Keys);
                    string relicSetValueText = string.Join('\n', activeSet.Values);

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
            MemoryStream stream = new();
            await background.SaveAsJpegAsync(stream, m_JpegEncoder);
            stream.Position = 0;

            m_Logger.LogInformation("Successfully generated character card for {CharacterName}", charInfo.Base.Name);
            return stream;
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Failed to generate character card for Character {CharacterInfo}",
                charInfo.ToString());
            throw new CommandException("An error occurred while generating the character card", ex);
        }
        finally
        {
            foreach (IDisposable resource in disposableResources) resource.Dispose();
        }
    }

    private async Task<Image<Rgba32>> CreateRelicSlotImageAsync(Relic relic)
    {
        m_Logger.LogTrace("Creating relic slot image for {RelicId}", relic.Id);
        try
        {
            string path = string.Format(BasePath, relic.Id);
            m_Logger.LogTrace("Loading relic image from {Path}", path);

            Image<Rgba32> relicImage = await Image.LoadAsync<Rgba32>(
                await m_ImageRepository.DownloadFileToStreamAsync(path));

            Image<Rgba32> template = CreateRelicSlot();
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
                Image<Rgba32> stars = ImageUtility.GenerateStarRating(relic.Rarity.GetValueOrDefault(1));
                stars.Mutate(x => x.Resize(0, 25));
                ctx.DrawImage(stars, new Point(120, 130), 1f);

                for (int i = 0; i < relic.SubPropertyList.Count; i++)
                {
                    RelicStatProperty subStat = relic.SubPropertyList[i];
                    Image subStatImage = m_StatImages[subStat.PropertyType!.Value];
                    int xOffset = i % 2 * 290;
                    int yOffset = i / 2 * 80;
                    Color color = Color.White;
                    if (subStat.PropertyType is 2 or 5 or 8)
                    {
                        Image<Rgba32> dim = subStatImage.CloneAs<Rgba32>();
                        dim.Mutate(x => x.Brightness(0.5f));
                        ctx.DrawImage(dim, new Point(375 + xOffset, 26 + yOffset), 1f);
                        color = Color.FromRgb(128, 128, 128);
                    }
                    else
                    {
                        ctx.DrawImage(subStatImage, new Point(375 + xOffset, 26 + yOffset), 1f);
                    }

                    ctx.DrawText(subStat.Value, m_NormalFont, color, new PointF(439 + xOffset, 30 + yOffset));

                    string rolls = string.Concat(Enumerable.Repeat('.', subStat.Times.GetValueOrDefault(0) + 1));
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
            string path = $"genshin_relic_template_{position}";
            m_Logger.LogDebug("Loading template relic image from {Path}", path);

            Image<Rgba32> relicImage = await Image.LoadAsync<Rgba32>(
                await m_ImageRepository.DownloadFileToStreamAsync(path));
            relicImage.Mutate(x => x.Resize(new Size(0, 150), KnownResamplers.Bicubic, true));
            Image<Rgba32> template = CreateRelicSlot();
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
        Color color = element switch
        {
            "Pyro" => Color.ParseHex("#8F321A"),
            "Hydro" => Color.ParseHex("#2059B9"),
            "Electro" => Color.ParseHex("#7D38B3"),
            "Dendro" => Color.ParseHex("#006D20"),
            "Cryo" => Color.ParseHex("#40A8BB"),
            "Geo" => Color.ParseHex("#806A00"),
            "Anemo" => Color.ParseHex("#1B9A89"),
            _ => Color.SlateGray
        };

        if (element == "_")
            m_Logger.LogWarning("Unknown element type: {Element}, using default color", element);

        return color;
    }
}
