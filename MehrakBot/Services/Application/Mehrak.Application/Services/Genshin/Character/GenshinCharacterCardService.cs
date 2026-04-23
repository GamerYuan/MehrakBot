#region

using System.Numerics;
using System.Text.RegularExpressions;
using Mehrak.Application.Renderers;
using Mehrak.Application.Renderers.Extensions;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.GameApi.Genshin.Types;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Services.Genshin.Character;

internal class GenshinCharacterCardService : CardServiceBase<GenshinCharacterInformation>
{
    private Dictionary<int, Image> m_StatImages = null!;
    private Image<Rgba32> m_RelicSlotTemplate = null!;

    private const string StatsPath = FileNameFormat.Genshin.StatsName;

    public GenshinCharacterCardService(IImageRepository imageRepository, ILogger<GenshinCharacterCardService> logger, IApplicationMetrics metrics)
        : base(
            "Genshin Character",
            imageRepository,
            logger,
            metrics,
            LoadFonts("Assets/Fonts/genshin.ttf", titleSize: 64, normalSize: 40, mediumSize: 32, smallSize: 28))
    { }

    public override async Task LoadStaticResourcesAsync(CancellationToken cancellationToken = default)
    {
        int[] statIds =
            [1, 2, 3, 4, 5, 6, 7, 8, 9, 20, 22, 23, 26, 27, 28, 30, 40, 41, 42, 43, 44, 45, 46, 2000, 2001, 2002];

        Logger.LogDebug("Loading {Count} stat icons", statIds.Length);
        m_StatImages = await statIds.ToAsyncEnumerable().Select(async (x, token) =>
        {
            try
            {
                var path = string.Format(StatsPath, x);
                var image = await Image.LoadAsync(await ImageRepository.DownloadFileToStreamAsync(path), token);
                image.Mutate(ctx => ctx.Resize(new Size(48, 0), KnownResamplers.Bicubic, true));
                return new KeyValuePair<int, Image>(x, image);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to load stat icon {StatId}", x);
                return new KeyValuePair<int, Image>(x, new Image<Rgba32>(48, 48));
            }
        }).ToDictionaryAsync(kvp => kvp.Key, kvp => kvp.Value, cancellationToken: cancellationToken);

        m_RelicSlotTemplate = new Image<Rgba32>(970, 170);
        m_RelicSlotTemplate.Mutate(ctx =>
        {
            ctx.Fill(new Rgba32(0, 0, 0, 0.25f));
            ctx.ApplyRoundedCorners(15);
        });

        StaticBackground = (await Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync("genshin_bg", cancellationToken),
            cancellationToken)).CloneAs<Rgba32>();

        Logger.LogInformation(
            "Resources initialized successfully with {Count} icons.",
            m_StatImages.Count);
    }

    public override async Task RenderCardAsync(
        Image<Rgba32> background,
        ICardGenerationContext<GenshinCharacterInformation> context,
        DisposableBag disposables,
        CancellationToken cancellationToken = default)
    {
        var charInfo = context.Data;

        if (StaticBackground == null || Fonts.Medium == null || Fonts.Small == null)
            throw new CommandException("An error occurred when generating Genshin Character card");

        var characterPortraitTask =
            Image.LoadAsync<Rgba32>(
                await ImageRepository.DownloadFileToStreamAsync(charInfo.Base.ToImageName(), cancellationToken));

        Task<Image<Rgba32>> weaponImageTask;

        if (charInfo.Weapon.PromoteLevel >= 2 && await ImageRepository.FileExistsAsync(charInfo.Weapon.ToAscendedImageName()))
        {
            weaponImageTask = Image.LoadAsync<Rgba32>(
                await ImageRepository.DownloadFileToStreamAsync(charInfo.Weapon.ToAscendedImageName(), cancellationToken));
        }
        else
        {
            if (charInfo.Weapon.PromoteLevel >= 2)
                Logger.LogInformation("Ascended icon not found for Weapon {Weapon}, falling back to default icon",
                    charInfo.Weapon.Name);
            weaponImageTask = Image.LoadAsync<Rgba32>(
                await ImageRepository.DownloadFileToStreamAsync(charInfo.Weapon.ToBaseImageName(), cancellationToken));
        }

        Task<(bool Active, Image Image)>[] constellationTasks =
        [
            .. charInfo.Constellations.Select(async x =>
            {
                var image = await Image.LoadAsync(
                    await ImageRepository.DownloadFileToStreamAsync(x.ToImageName(), cancellationToken), cancellationToken);
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
                        await ImageRepository.DownloadFileToStreamAsync(x.ToImageName(charInfo.Base.Id), cancellationToken), cancellationToken);
                    return (Data: x, Image: image);
                }).Reverse()
        ];

        Task<Image<Rgba32>>[] relicImageTasks =
        [
            .. Enumerable.Range(0, 5).Select(async i =>
            {
                var relic = charInfo.Relics.FirstOrDefault(x => x.Pos == i + 1);
                if (relic != null)
                {
                    var relicImage = await CreateRelicSlotImageAsync(relic, disposables, cancellationToken);
                    return relicImage;
                }
                else
                {
                    var templateRelicImage = await CreateTemplateRelicSlotImageAsync(i + 1, cancellationToken);
                    return templateRelicImage;
                }
            })
        ];

        if (charInfo.Constellations?.FirstOrDefault(x => x.Pos == 3)?.IsActived ?? false)
            AssignConstEffects(charInfo.Constellations[2], charInfo.Skills);

        if (charInfo.Constellations?.FirstOrDefault(x => x.Pos == 5)?.IsActived ?? false)
            AssignConstEffects(charInfo.Constellations[4], charInfo.Skills);

        var characterPortrait = await characterPortraitTask;
        disposables.Add(characterPortrait);

        var weaponImage = await weaponImageTask;
        disposables.Add(weaponImage);

        (bool Active, Image Image)[] constellationIcons = [.. await Task.WhenAll(constellationTasks)];
        disposables.AddRange(constellationIcons.Select(c => c.Image));

        (Skill Data, Image Image)[] skillIcons = [.. await Task.WhenAll(skillTasks)];
        disposables.AddRange(skillIcons.Select(s => s.Image));

        Image<Rgba32>[] relics = [.. await Task.WhenAll(relicImageTasks)];
        disposables.AddRange(relics);

        Logger.LogDebug("Processing {Count} relic images", charInfo.Relics.Count);
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

        Logger.LogDebug("Found {Count} active relic sets", activeSet.Count);

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
            ctx.DrawImage(StaticBackground, PixelColorBlendingMode.Overlay, 1f);

            var textColor = Color.White;

            ctx.DrawImage(characterPortrait,
                new Point((1280 - characterPortrait.Width) / 2, 100 + (1080 - characterPortrait.Height) / 2),
                1f);

            ctx.DrawTextWithShadow(charInfo.Base.Name, Fonts.Title, new PointF(70, 55), textColor);

            var ascLevel = context.GetParameter<int?>("ascension");

            if (ascLevel != null)
            {
                ctx.DrawTextWithShadow($"Lv. {charInfo.Base.Level}/{ascLevel.Value}", Fonts.Normal,
                    new PointF(70, 135), textColor);
            }
            else
            {
                ctx.DrawTextWithShadow($"Lv. {charInfo.Base.Level}", Fonts.Normal,
                    new PointF(70, 135), textColor);
            }

            for (var i = 0; i < skillIcons.Length; i++)
            {
                var skill = skillIcons[i];
                var offset = i * 150;
                ctx.DrawCenteredIcon(skill.Image, new PointF(120, 900 - offset), 60, 20, Color.DarkSlateGray,
                    backgroundColor, 5f);
                ctx.DrawCenteredTextInEllipse(
                    skill.Data.Level.ToString()!,
                    new PointF(120, 960 - offset),
                    25,
                    new EllipseTextStyle(
                        Fonts.Medium,
                        textColor,
                        skill.Data.IsConstAffected ? Color.DodgerBlue : Color.DarkGray));
            }

            ctx.DrawTextWithShadow(context.GameProfile.Nickname, Fonts.Normal,
                new PointF(60, 1000), textColor);

            ctx.DrawTextWithShadow(context.GameProfile.GameUid, Fonts.Small,
                new PointF(60, 1040), textColor);

            for (var i = 0; i < constellationIcons.Length; i++)
            {
                var constellation = constellationIcons[i];
                if (!constellation.Active)
                    constellation.Image.Mutate(x => x.Brightness(0.5f));
                var offset = i * 140;
                ctx.DrawCenteredIcon(constellation.Image, new PointF(1050, 1000 - offset), 50, 10,
                    Color.DarkSlateGray, backgroundColor, 5f);
            }

            ctx.DrawImage(weaponImage, new Point(1200, 40), 1f);
            ctx.DrawImage(ImageUtility.GenerateStarRating(charInfo.Weapon.Rarity.GetValueOrDefault(1)),
                new Point(1220, 240), 1f);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(1450, 120),
                VerticalAlignment = VerticalAlignment.Bottom,
                WrappingLength = 650
            }, charInfo.Weapon.Name, textColor);
            ctx.DrawText('R' + charInfo.Weapon.AffixLevel!.Value.ToString(), Fonts.Normal, textColor,
                new PointF(1450, 160));
            ctx.DrawText($"Lv. {charInfo.Weapon.Level}", Fonts.Normal, textColor, new PointF(1550, 160));
            var statSize =
                TextMeasurer.MeasureSize(charInfo.Weapon.MainProperty.Final, new TextOptions(Fonts.Normal));
            Image<Rgba32> statBackground = new(80 + (int)statSize.Width, 60);
            statBackground.Mutate(x =>
            {
                x.Fill(new Rgba32(0, 0, 0, 0.45f));
                x.ApplyRoundedCorners(10);
            });
            ctx.DrawImage(statBackground, new Point(1450, 230), 1f);
            ctx.DrawImage(m_StatImages[charInfo.Weapon.MainProperty.PropertyType!.Value], new Point(1455, 236),
                1f);
            ctx.DrawText(charInfo.Weapon.MainProperty.Final, Fonts.Normal, textColor, new PointF(1514, 240));
            if (charInfo.Weapon.SubProperty != null)
            {
                var substatSize =
                    TextMeasurer.MeasureSize(charInfo.Weapon.SubProperty.Final, new TextOptions(Fonts.Normal));
                Image<Rgba32> substatBackground = new(80 + (int)substatSize.Width, 60);
                substatBackground.Mutate(x =>
                {
                    x.Fill(new Rgba32(0, 0, 0, 0.45f));
                    x.ApplyRoundedCorners(10);
                });
                ctx.DrawImage(substatBackground, new Point(1630, 230), 1f);
                ctx.DrawImage(m_StatImages[charInfo.Weapon.SubProperty.PropertyType!.Value], new Point(1635, 236),
                    1f);
                ctx.DrawText(charInfo.Weapon.SubProperty.Final, Fonts.Normal, textColor, new PointF(1694, 240));
            }

            var spacing = 700 / stats.Length;

            for (var i = 0; i < stats.Length; i++)
            {
                var stat = stats[i];
                var y = 360 + spacing * i;
                var isBase = StatMappingUtility.IsBaseStat(stat.PropertyType!.Value);

                ctx.DrawStatLine(
                    new StatLineData(
                        StatMappingUtility.GenshinMapping[stat.PropertyType.Value],
                        stat.Final,
                        isBase ? stat.Base : null,
                        isBase && int.Parse(stat.Final.TrimEnd('%')) > int.Parse(stat.Base.TrimEnd('%')) ? $"+{stat.Add}" : null),
                    new StatLineStyle(
                        m_StatImages.GetValueOrDefault(stat.PropertyType.Value),
                        Fonts.Normal,
                        textColor,
                        Fonts.Small,
                        Color.LightGray,
                        Color.LightGreen),
                    new PointF(1200, y),
                    900);
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

                ctx.DrawText(new RichTextOptions(Fonts.Small)
                {
                    Origin = new Vector2(2750, 1020),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    TextAlignment = TextAlignment.End,
                    LineSpacing = 1.5f,
                    WrappingLength = 500
                }, relicSetText, Color.White);

                ctx.DrawText(new RichTextOptions(Fonts.Small)
                {
                    Origin = new Vector2(2800, 1020),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    LineSpacing = 1.5f
                }, relicSetValueText, Color.White);
            }
            else
            {
                ctx.DrawText(new RichTextOptions(Fonts.Small)
                {
                    Origin = new Vector2(2725, 1020),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                }, "No active set", Color.White);
            }
        });
    }

    private async Task<Image<Rgba32>> CreateRelicSlotImageAsync(Relic relic, DisposableBag disposables, CancellationToken cancellationToken)
    {
        var path = relic.ToImageName();

        var relicImage = await Image.LoadAsync<Rgba32>(
            await ImageRepository.DownloadFileToStreamAsync(path, cancellationToken), cancellationToken);
        disposables.Add(relicImage);

        var template = CreateRelicSlot();
        template.Mutate(ctx =>
        {
            ctx.DrawImage(relicImage, new Point(-40, -40), 1f);
            ctx.DrawImage(m_StatImages[relic.MainProperty.PropertyType!.Value], new Point(280, 20), 1f);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                TextAlignment = TextAlignment.End,
                HorizontalAlignment = HorizontalAlignment.Right,
                Origin = new Vector2(320, 70)
            }, relic.MainProperty.Value, Color.White);
            ctx.DrawText(new RichTextOptions(Fonts.Small!)
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

                ctx.DrawText(subStat.Value, Fonts.Normal, color, new PointF(439 + xOffset, 30 + yOffset));

                var rolls = string.Concat(Enumerable.Repeat('.', subStat.Times.GetValueOrDefault(0) + 1));
                ctx.DrawText(rolls, Fonts.Normal, color, new PointF(575 + xOffset, 15 + yOffset));
            }

            relicImage.Dispose();
        });

        return template;
    }

    private async Task<Image<Rgba32>> CreateTemplateRelicSlotImageAsync(int position, CancellationToken cancellationToken)
    {
        var path = $"genshin_relic_template_{position}";

        var relicImage = await Image.LoadAsync<Rgba32>(
            await ImageRepository.DownloadFileToStreamAsync(path, cancellationToken), cancellationToken);
        relicImage.Mutate(x => x.Resize(new Size(0, 150), KnownResamplers.Bicubic, true));
        var template = CreateRelicSlot();
        template.Mutate(ctx =>
        {
            ctx.DrawImage(relicImage, new Point(25, 5), 1f);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
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
        Logger.LogTrace("Getting background color for element: {Element}", element);
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
            Logger.LogWarning("Unknown element type: {Element}, using default color", element);

        return color;
    }

    private void AssignConstEffects(Constellation activeConst, List<Skill> skills)
    {
        if (activeConst.Effect == null) return;

        var skillNames = Regex.Matches(activeConst.Effect, @"<color=#FFD780FF>(.*?)<\/color>")
            .Select(x => x.Groups[1].Value.Replace("Normal Attack: ", "")).ToList();

        if (skillNames.Count == 0)
        {
            Logger.LogWarning("Expected skill name but found none in constellation effect: {Effect}",
                activeConst.Effect);
            return;
        }

        foreach (var skill in skills.Where(skill => skillNames.Contains(skill.Name)))
        {
            skill.IsConstAffected = true;
        }
    }
}
