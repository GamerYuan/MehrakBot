#region

using System.Numerics;
using System.Text.RegularExpressions;
using Mehrak.Application.Renderers.Extensions;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Renderers;
using Mehrak.Application.Shared.Renderers.Extensions;
using Mehrak.Application.Shared.Utility;
using Mehrak.Domain.Character.Models;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Models;
using Mehrak.Domain.Shared.Common;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.User.Abstractions;
using Mehrak.GameApi.Genshin.Types;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Genshin.Character;

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

        m_RelicSlotTemplate = new Image<Rgba32>(970, 170, Color.Transparent.ToPixel<Rgba32>());
        m_RelicSlotTemplate.Mutate(ctx =>
        {
            ctx.Paint(canvas =>
            {
                canvas.Fill(Brushes.Solid(Color.FromPixel(new Rgba32(0, 0, 0, 0.25f))),
                    new Rectangle(0, 0, 970, 170));
            });
            ctx.ApplyRoundedCorners(15);
        });

        StaticBackground = await Image.LoadAsync<Rgba32>(
            await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Genshin.BackgroundName, cancellationToken),
            cancellationToken);

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
            LoadImageFromRepositoryAsync<Rgba32>(charInfo.Base.ToImageName(), disposables, cancellationToken);

        Task<Image<Rgba32>> weaponImageTask;

        if (charInfo.Weapon.PromoteLevel >= 2 && await ImageRepository.FileExistsAsync(charInfo.Weapon.ToAscendedImageName()))
        {
            weaponImageTask = LoadImageFromRepositoryAsync<Rgba32>(
                charInfo.Weapon.ToAscendedImageName(), disposables, cancellationToken);
        }
        else
        {
            if (charInfo.Weapon.PromoteLevel >= 2)
                Logger.LogInformation("Ascended icon not found for Weapon {Weapon}, falling back to default icon",
                    charInfo.Weapon.Name);
            weaponImageTask = LoadImageFromRepositoryAsync<Rgba32>(
                charInfo.Weapon.ToBaseImageName(), disposables, cancellationToken);
        }

        Task<(bool Active, Image Image)>[] constellationTasks =
        [
            .. charInfo.Constellations.Select(async x =>
            {
                var image = await LoadImageFromRepositoryAsync(x.ToImageName(), disposables, cancellationToken);
                return (Active: x.IsActived.GetValueOrDefault(false), Image: image);
            }).Reverse()
        ];

        Task<(Skill Data, Image Image)>[] skillTasks =
        [
            .. charInfo.Skills
                .Where(x => x.SkillType!.Value == 1 && !x.Desc.Contains("Alternate Sprint"))
                .Select(async x =>
                {
                    var image = await LoadImageFromRepositoryAsync(x.ToImageName(charInfo.Base.Id), disposables, cancellationToken);
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

        var portraitConfig = context.GetParameter<CharacterPortraitConfig>("portraitConfig");
        characterPortrait.Mutate(ctx =>
        {
            ctx.CropTransparentPixels();

            if (portraitConfig?.TargetScale > 0f)
            {
                var scale = portraitConfig.TargetScale.Value;
                ctx.Resize((int)(ctx.GetCurrentSize().Width * scale), 0, KnownResamplers.Lanczos3);
            }
            else
            {
                var size = ctx.GetCurrentSize();
                if (size.Width >= size.Height)
                    ctx.Resize(0, (int)Math.Round(1280 * Math.Min(1.2 * size.Height / size.Width, 1f)), KnownResamplers.Lanczos3);
                else
                    ctx.Resize(1400, 0, KnownResamplers.Lanczos3);

                size = ctx.GetCurrentSize();
                if (size.Height > 1280)
                    ctx.Resize(0, 1280, KnownResamplers.Lanczos3);

                size = ctx.GetCurrentSize();
                if (size.Width > 1280)
                    ctx.Crop(new Rectangle((size.Width - 1280) / 2, 0, 1280, size.Height));
            }

            var enableFade = portraitConfig?.EnableGradientFade ?? true;
            if (enableFade &&
                (portraitConfig?.GradientFadeStart ?? 0.75f) > 0f)
                ctx.ApplyGradientFade(portraitConfig?.GradientFadeStart ?? 0.75f);
        });

        var weaponImage = await weaponImageTask;

        (bool Active, Image Image)[] constellationIcons = [.. await Task.WhenAll(constellationTasks)];

        (Skill Data, Image Image)[] skillIcons = [.. await Task.WhenAll(skillTasks)];

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
            ctx.Paint(canvas =>
            {
                canvas.Fill(Brushes.Solid(backgroundColor), new Rectangle(0, 0, background.Width, background.Height));
            });

            ctx.Paint(canvas =>
            {
                _ = canvas.SaveLayer(new GraphicsOptions { ColorBlendingMode = PixelColorBlendingMode.Overlay });
                canvas.DrawImage(StaticBackground, StaticBackground.Bounds,
                    new RectangleF(0, 0, background.Width, background.Height), KnownResamplers.Bicubic);
                canvas.Restore();

                var textColor = Color.White;

                var offsetX = portraitConfig?.OffsetX ?? 0;
                var offsetY = portraitConfig?.OffsetY ?? 0;
                canvas.DrawImage(characterPortrait, characterPortrait.Bounds,
                    new RectangleF((1280 - characterPortrait.Width) / 2 + offsetX, 100 + (1080 - characterPortrait.Height) / 2 + offsetY,
                        characterPortrait.Width, characterPortrait.Height),
                    KnownResamplers.Bicubic);

                canvas.DrawTextWithShadow(charInfo.Base.Name, Fonts.Title, new PointF(70, 55), textColor);

                var ascLevel = context.GetParameter<int?>("ascension");

                if (ascLevel != null)
                {
                    canvas.DrawTextWithShadow($"Lv. {charInfo.Base.Level}/{ascLevel.Value}", Fonts.Normal,
                        new PointF(70, 135), textColor);
                }
                else
                {
                    canvas.DrawTextWithShadow($"Lv. {charInfo.Base.Level}", Fonts.Normal,
                        new PointF(70, 135), textColor);
                }

                for (var i = 0; i < skillIcons.Length; i++)
                {
                    var skill = skillIcons[i];
                    var offset = i * 150;
                    canvas.DrawCenteredIcon(skill.Image, new PointF(120, 900 - offset), 60, 10, Color.DarkSlateGray,
                        backgroundColor, 5f);
                    canvas.DrawCenteredTextInEllipse(
                        skill.Data.Level.ToString()!,
                        new PointF(120, 960 - offset),
                        25,
                        new EllipseTextStyle(
                            Fonts.Medium,
                            textColor,
                            skill.Data.IsConstAffected ? Color.DodgerBlue : Color.DarkGray));
                }

                canvas.DrawTextWithShadow(context.GameProfile.Nickname, Fonts.Normal,
                    new PointF(60, 1000), textColor);

                canvas.DrawTextWithShadow(context.GameProfile.GameUid, Fonts.Small,
                    new PointF(60, 1040), textColor);

                for (var i = 0; i < constellationIcons.Length; i++)
                {
                    var constellation = constellationIcons[i];
                    if (!constellation.Active)
                        constellation.Image.Mutate(x => x.Brightness(0.5f));
                    var offset = i * 140;
                    canvas.DrawCenteredIcon(constellation.Image, new PointF(1050, 1000 - offset), 50, 5,
                        Color.DarkSlateGray, backgroundColor, 5f);
                }

                canvas.DrawImage(weaponImage, weaponImage.Bounds,
                    new RectangleF(1200, 40, weaponImage.Width, weaponImage.Height), KnownResamplers.Bicubic);
                using var weaponStars = ImageUtility.CreateStarRatingImage(charInfo.Weapon.Rarity.GetValueOrDefault(1));
                canvas.DrawImage(weaponStars, weaponStars.Bounds,
                    new RectangleF(1220, 240, weaponStars.Width, weaponStars.Height), KnownResamplers.Bicubic);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(1450, 120),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    WrappingLength = 650
                }, charInfo.Weapon.Name, Brushes.Solid(textColor), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new PointF(1450, 160)
                }, 'R' + charInfo.Weapon.AffixLevel!.Value.ToString(), Brushes.Solid(textColor), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new PointF(1550, 160)
                }, $"Lv. {charInfo.Weapon.Level}", Brushes.Solid(textColor), null);
                var statSize =
                    TextMeasurer.MeasureBounds(charInfo.Weapon.MainProperty.Final, new TextOptions(Fonts.Normal));
                Image<Rgba32> statBackground = new(80 + (int)statSize.Width, 60, Color.Transparent.ToPixel<Rgba32>());
                statBackground.Mutate(x =>
                {
                    x.Paint(canvas2 =>
                    {
                        canvas2.Fill(Brushes.Solid(Color.FromPixel(new Rgba32(0, 0, 0, 0.45f))),
                            new Rectangle(0, 0, statBackground.Width, statBackground.Height));
                    });
                    x.ApplyRoundedCorners(10);
                });
                canvas.DrawImage(statBackground, statBackground.Bounds,
                    new RectangleF(1450, 230, statBackground.Width, statBackground.Height), KnownResamplers.Bicubic);
                canvas.DrawImage(m_StatImages[charInfo.Weapon.MainProperty.PropertyType!.Value],
                    m_StatImages[charInfo.Weapon.MainProperty.PropertyType!.Value].Bounds,
                    new RectangleF(1455, 236, m_StatImages[charInfo.Weapon.MainProperty.PropertyType!.Value].Width,
                        m_StatImages[charInfo.Weapon.MainProperty.PropertyType!.Value].Height),
                    KnownResamplers.Bicubic);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new PointF(1514, 240)
                }, charInfo.Weapon.MainProperty.Final, Brushes.Solid(textColor), null);
                if (charInfo.Weapon.SubProperty != null)
                {
                    var substatSize =
                        TextMeasurer.MeasureBounds(charInfo.Weapon.SubProperty.Final, new TextOptions(Fonts.Normal));
                    Image<Rgba32> substatBackground = new(80 + (int)substatSize.Width, 60);
                    substatBackground.Mutate(x =>
                    {
                        x.Paint(canvas2 =>
                        {
                            canvas2.Fill(Brushes.Solid(Color.FromPixel(new Rgba32(0, 0, 0, 0.45f))),
                                new Rectangle(0, 0, substatBackground.Width, substatBackground.Height));
                        });
                        x.ApplyRoundedCorners(10);
                    });
                    canvas.DrawImage(substatBackground, substatBackground.Bounds,
                        new RectangleF(1630, 230, substatBackground.Width, substatBackground.Height), KnownResamplers.Bicubic);
                    canvas.DrawImage(m_StatImages[charInfo.Weapon.SubProperty.PropertyType!.Value],
                        m_StatImages[charInfo.Weapon.SubProperty.PropertyType!.Value].Bounds,
                        new RectangleF(1635, 236, m_StatImages[charInfo.Weapon.SubProperty.PropertyType!.Value].Width,
                            m_StatImages[charInfo.Weapon.SubProperty.PropertyType!.Value].Height),
                        KnownResamplers.Bicubic);
                    canvas.DrawText(new RichTextOptions(Fonts.Normal)
                    {
                        Origin = new PointF(1694, 240)
                    }, charInfo.Weapon.SubProperty.Final, Brushes.Solid(textColor), null);
                }

                var spacing = 700 / stats.Length;

                for (var i = 0; i < stats.Length; i++)
                {
                    var stat = stats[i];
                    var y = 360 + spacing * i;
                    var isBase = StatMappingUtility.IsBaseStat(stat.PropertyType!.Value);

                    canvas.DrawStatLine(
                        new StatLineData(
                            StatMappingUtility.GenshinMapping[stat.PropertyType.Value],
                            stat.Final,
                            isBase ? stat.Base : null,
                            isBase && float.Parse(stat.Final.TrimEnd('%')) > float.Parse(stat.Base.TrimEnd('%')) ? $"+{stat.Add}" : null),
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
                    canvas.DrawImage(relic, relic.Bounds,
                        new RectangleF(2200, 40 + i * 185, relic.Width, relic.Height), KnownResamplers.Bicubic);
                }

                if (activeSet.Count > 0)
                {
                    var relicSetText = string.Join('\n', activeSet.Keys);
                    var relicSetValueText = string.Join('\n', activeSet.Values);

                    canvas.DrawText(new RichTextOptions(Fonts.Small)
                    {
                        Origin = new Vector2(2750, 1020),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        TextAlignment = TextAlignment.End,
                        LineSpacing = 1.5f,
                        WrappingLength = 500
                    }, relicSetText, Brushes.Solid(Color.White), null);

                    canvas.DrawText(new RichTextOptions(Fonts.Small)
                    {
                        Origin = new Vector2(2800, 1020),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        LineSpacing = 1.5f
                    }, relicSetValueText, Brushes.Solid(Color.White), null);
                }
                else
                {
                    canvas.DrawText(new RichTextOptions(Fonts.Small)
                    {
                        Origin = new Vector2(2725, 1020),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }, "No active set", Brushes.Solid(Color.White), null);
                }
            });
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
            ctx.Paint(canvas =>
            {
                canvas.DrawImage(relicImage, relicImage.Bounds,
                    new RectangleF(-40, -40, relicImage.Width, relicImage.Height), KnownResamplers.Bicubic);
                var statImg = m_StatImages[relic.MainProperty.PropertyType!.Value];
                canvas.DrawImage(statImg, statImg.Bounds,
                    new RectangleF(280, 20, statImg.Width, statImg.Height), KnownResamplers.Bicubic);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    TextAlignment = TextAlignment.End,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Origin = new Vector2(320, 70)
                }, relic.MainProperty.Value, Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Small!)
                {
                    TextAlignment = TextAlignment.End,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Origin = new Vector2(320, 130)
                }, $"+{relic.Level!.Value}", Brushes.Solid(Color.White), null);
                using var stars = ImageUtility.CreateStarRatingImage(relic.Rarity.GetValueOrDefault(1));
                stars.Mutate(x => x.Resize(0, 25));
                canvas.DrawImage(stars, stars.Bounds,
                    new RectangleF(120, 130, stars.Width, stars.Height), KnownResamplers.Bicubic);

                for (var i = 0; i < relic.SubPropertyList.Count; i++)
                {
                    var subStat = relic.SubPropertyList[i];
                    var subStatImage = m_StatImages[subStat.PropertyType!.Value];
                    var xOffset = i % 2 * 290;
                    var yOffset = i / 2 * 80;
                    var color = Color.White;
                    if (subStat.PropertyType is 2 or 5 or 8)
                    {
                        using var dim = subStatImage.Clone(x => x.Brightness(0.5f));
                        canvas.DrawImage(dim, dim.Bounds,
                            new RectangleF(375 + xOffset, 26 + yOffset, dim.Width, dim.Height), KnownResamplers.Bicubic);
                        color = Color.FromPixel(new Rgb24(128, 128, 128));
                    }
                    else
                    {
                        canvas.DrawImage(subStatImage, subStatImage.Bounds,
                            new RectangleF(375 + xOffset, 26 + yOffset, subStatImage.Width, subStatImage.Height), KnownResamplers.Bicubic);
                    }

                    canvas.DrawText(new RichTextOptions(Fonts.Normal)
                    {
                        Origin = new PointF(439 + xOffset, 30 + yOffset)
                    }, subStat.Value, Brushes.Solid(color), null);

                    var rolls = string.Concat(Enumerable.Repeat('.', subStat.Times.GetValueOrDefault(0) + 1));
                    canvas.DrawText(new RichTextOptions(Fonts.Normal)
                    {
                        Origin = new PointF(575 + xOffset, 15 + yOffset)
                    }, rolls, Brushes.Solid(color), null);
                }

                relicImage.Dispose();
            });
        });

        return template;
    }

    private async Task<Image<Rgba32>> CreateTemplateRelicSlotImageAsync(int position, CancellationToken cancellationToken)
    {
        var path = string.Format(FileNameFormat.Genshin.RelicTemplateName, position);

        var relicImage = await Image.LoadAsync<Rgba32>(
            await ImageRepository.DownloadFileToStreamAsync(path, cancellationToken), cancellationToken);
        relicImage.Mutate(x => x.Resize(new Size(0, 150), KnownResamplers.Bicubic, true));
        var template = CreateRelicSlot();
        template.Mutate(ctx =>
        {
            ctx.Paint(canvas =>
            {
                canvas.DrawImage(relicImage, relicImage.Bounds,
                    new RectangleF(25, 5, relicImage.Width, relicImage.Height), KnownResamplers.Bicubic);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(525, 95),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                }, "No Artifact", Brushes.Solid(Color.White), null);
            });
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
