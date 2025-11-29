#region

using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Services.Genshin.Character;

internal class GenshinCharacterCardService : ICardService<GenshinCharacterInformation>, IAsyncInitializable
{
    private readonly IImageRepository m_ImageRepository;
    private readonly ILogger<GenshinCharacterCardService> m_Logger;

    private Dictionary<int, Image> m_StatImages = null!;

    private const string StatsPath = FileNameFormat.Genshin.StatsName;

    private readonly Font m_SmallFont;
    private readonly Font m_MediumFont;
    private readonly Font m_NormalFont;
    private readonly Font m_TitleFont;

    private readonly JpegEncoder m_JpegEncoder;
    private readonly Image<Rgba32> m_RelicSlotTemplate;

    public GenshinCharacterCardService(IImageRepository imageRepository, ILogger<GenshinCharacterCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_Logger = logger;

        FontCollection collection = new();
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
        m_StatImages = await statIds.ToAsyncEnumerable().Select(async (x, token) =>
        {
            try
            {
                var path = string.Format(StatsPath, x);
                var image = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(path), token);
                image.Mutate(ctx => ctx.Resize(new Size(48, 0), KnownResamplers.Bicubic, true));
                return new KeyValuePair<int, Image>(x, image);
            }
            catch (Exception ex)
            {
                m_Logger.LogError(ex, "Failed to load stat icon {StatId}", x);
                return new KeyValuePair<int, Image>(x, new Image<Rgba32>(48, 48));
            }
        }).ToDictionaryAsync(kvp => kvp.Key, kvp => kvp.Value, cancellationToken: cancellationToken);

        m_Logger.LogInformation(
            "Resources initialized successfully with {Count} icons.",
            m_StatImages.Count);

        m_Logger.LogInformation(LogMessage.ServiceInitialized, nameof(GenshinCharacterCardService));
    }

    public async Task<Stream> GetCardAsync(ICardGenerationContext<GenshinCharacterInformation> context)
    {
        m_Logger.LogInformation(LogMessage.CardGenStartInfo, "Character", context.UserId);
        var stopwatch = Stopwatch.StartNew();

        var charInfo = context.Data;

        List<IDisposable> disposableResources = [];

        try
        {
            // Prepare all image loading tasks
            var overlayTask = Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("genshin_bg"));
            Image<Rgba32> background = new(3240, 1080);
            disposableResources.Add(background);

            var characterPortraitTask =
                Image.LoadAsync<Rgba32>(
                    await m_ImageRepository.DownloadFileToStreamAsync(charInfo.Base.ToImageName()));

            var weaponImageTask =
                Image.LoadAsync<Rgba32>(
                    await m_ImageRepository.DownloadFileToStreamAsync(charInfo.Base.Weapon.ToImageName()));

            Task<(bool Active, Image Image)>[] constellationTasks =
            [
                .. charInfo.Constellations.Select(async x =>
                {
                    var image = await Image.LoadAsync(
                        await m_ImageRepository.DownloadFileToStreamAsync(x.ToImageName()));
                    return (Active: x.IsActived.GetValueOrDefault(false), Image: image);
                }).Reverse()
            ];

            Task<(Skill Data, Image Image)>[] skillTasks =
            [
                .. charInfo.Skills
                    .Where(x => x.SkillType!.Value == 1 && !x.Desc.Contains("Alternate Sprint"))
                    .Select(async x =>
                    {
                        var image = await Image.LoadAsync(
                            await m_ImageRepository.DownloadFileToStreamAsync(x.ToImageName(charInfo.Base.Id)));
                        return (Data: x, Image: image);
                    }).Reverse()
            ];

            // Relic image tasks
            Task<Image<Rgba32>>[] relicImageTasks =
            [
                .. Enumerable.Range(0, 5).Select(async i =>
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
                })
            ];

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

            (bool Active, Image Image)[] constellationIcons = [.. await Task.WhenAll(constellationTasks)];
            disposableResources.AddRange(constellationIcons.Select(c => c.Image));

            (Skill Data, Image Image)[] skillIcons = [.. await Task.WhenAll(skillTasks)];
            disposableResources.AddRange(skillIcons.Select(s => s.Image));

            Image<Rgba32>[] relics = [.. await Task.WhenAll(relicImageTasks)];
            disposableResources.AddRange(relics);

            m_Logger.LogDebug("Processing {Count} relic images", charInfo.Relics.Count);
            Dictionary<RelicSet, int> relicActivation = [];
            for (var i = 0; i < 5; i++)
            {
                var relic = charInfo.Relics.FirstOrDefault(x => x.Pos == i + 1);
                if (relic == null) continue;
                if (!relicActivation.TryAdd(relic.RelicSet, 1))
                    relicActivation[relic.RelicSet]++;
            }

            Dictionary<string, int> activeSet = [];
            foreach (var relicSet in
                     relicActivation.Where(relicSet =>
                         relicSet.Key.Affixes.Any(x => relicSet.Value >= x.ActivationNumber.GetValueOrDefault(0))))
                activeSet.Add(relicSet.Key.Name, relicSet.Value);

            m_Logger.LogDebug("Found {Count} active relic sets", activeSet.Count);

            // Display based on the order
            // 1. Base stats
            // 2. EM
            // 3. Other stats
            StatProperty[] bonusStats =
            [
                .. charInfo.SelectedProperties.Where(x => float.Parse(x.Final.TrimEnd('%')) >
                                                          StatMappingUtility.GetDefaultValue(x.PropertyType!.Value,
                                                              Game.Genshin))
                    .OrderBy(x => x.PropertyType)
            ];

            StatProperty[] stats;
            if (bonusStats.Length >= 6)
                stats =
                [
                    .. charInfo.BaseProperties.Take(4)
                        .Where(x => float.Parse(x.Final.TrimEnd('%')) >
                                    StatMappingUtility.GetDefaultValue(x.PropertyType!.Value, Game.Genshin))
                        .Concat(bonusStats)
                        .DistinctBy(x => x.PropertyType)
                ];
            else
                stats =
                [
                    .. charInfo.BaseProperties.Take(4)
                        .Concat(charInfo.SelectedProperties.OrderBy(x => x.PropertyType))
                        .Take(7)
                ];

            background.Mutate(ctx =>
            {
                var backgroundColor = GetBackgroundColor(charInfo.Base.Element ?? "None");
                ctx.Fill(backgroundColor);
                ctx.DrawImage(overlay, PixelColorBlendingMode.Overlay, 1f);

                var textColor = Color.White;

                ctx.DrawImage(characterPortrait,
                    new Point((1280 - characterPortrait.Width) / 2, 100 + (1080 - characterPortrait.Height) / 2),
                    1f);

                ctx.DrawText(charInfo.Base.Name, m_TitleFont, Color.Black, new PointF(73, 58));
                ctx.DrawText(charInfo.Base.Name, m_TitleFont, textColor, new PointF(70, 55));
                ctx.DrawText($"Lv. {charInfo.Base.Level}", m_NormalFont, Color.Black, new PointF(73, 138));
                ctx.DrawText($"Lv. {charInfo.Base.Level}", m_NormalFont, textColor, new PointF(70, 135));

                for (var i = 0; i < skillIcons.Length; i++)
                {
                    var skill = skillIcons[i];
                    var offset = i * 150;
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

                ctx.DrawText(context.GameProfile.GameUid, m_SmallFont, textColor, new PointF(60, 1040));

                for (var i = 0; i < constellationIcons.Length; i++)
                {
                    var constellation = constellationIcons[i];
                    if (!constellation.Active)
                        constellation.Image.Mutate(x => x.Brightness(0.5f));
                    var offset = i * 140;
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
                var statSize =
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
                    var substatSize =
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

                var spacing = 700 / stats.Length;

                for (var i = 0; i < stats.Length; i++)
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
                        var xPos = 2100;
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

                for (var i = 0; i < relics.Length; i++)
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

            MemoryStream stream = new();
            await background.SaveAsJpegAsync(stream, m_JpegEncoder);
            stream.Position = 0;

            m_Logger.LogInformation(LogMessage.CardGenSuccess, "Character", context.UserId,
                stopwatch.ElapsedMilliseconds);
            return stream;
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, LogMessage.CardGenError, "Character", context.UserId,
                JsonSerializer.Serialize(context.Data));
            throw new CommandException("Failed to generate Character card", ex);
        }
        finally
        {
            foreach (var resource in disposableResources) resource.Dispose();
        }
    }

    private async Task<Image<Rgba32>> CreateRelicSlotImageAsync(Relic relic)
    {
        var path = relic.ToImageName();

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
            var stars = ImageUtility.GenerateStarRating(relic.Rarity.GetValueOrDefault(1));
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

    private async Task<Image<Rgba32>> CreateTemplateRelicSlotImageAsync(int position)
    {
        var path = $"genshin_relic_template_{position}";

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
