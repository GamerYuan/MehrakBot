#region

using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Mehrak.Application.Renderers.Extensions;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Renderers;
using Mehrak.Application.Shared.Renderers.Extensions;
using Mehrak.Application.Shared.Utility;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Models;
using Mehrak.Domain.Shared.Common;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Utility;
using Mehrak.Domain.User.Abstractions;
using Mehrak.GameApi.Genshin.Types;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

#endregion

namespace Mehrak.Application.Genshin.Character;

internal class GenshinCharacterCardService : CharacterCardServiceBase<GenshinCharacterInformation>
{
    private Dictionary<int, Image> m_StatImages = null!;
    private Dictionary<int, Image> m_DimmedStatImages = null!;
    private Image<Rgba32> m_RelicSlotTemplate = null!;
    private Image[] m_StarRatingImages = null!;
    private Image[] m_StarRatingImagesSmall = null!;
    private Image<Rgba32>[] m_RelicTemplateImages = null!;
    private Image<Rgba32> m_OverlayImage = null!;

    private const string StatsPath = FileNameFormat.Genshin.StatsName;

    protected override int DefaultPortraitWidth => 1400;
    protected override IResampler PortraitResampler => KnownResamplers.Bicubic;

    private const int FadeX = 1000;
    private const int FadeWidth = 150;

    public GenshinCharacterCardService(IImageRepository imageRepository,
        ILogger<GenshinCharacterCardService> logger, IApplicationMetrics metrics)
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

        m_DimmedStatImages = m_StatImages.ToDictionary(kvp => kvp.Key, kvp =>
        {
            var dimmed = kvp.Value.Clone(x => x.Brightness(0.5f));
            return dimmed;
        });

        m_StarRatingImages = new Image[6];
        m_StarRatingImagesSmall = new Image[6];
        for (var r = 1; r <= 5; r++)
        {
            m_StarRatingImages[r] = ImageUtility.CreateStarRatingImage(r);
            m_StarRatingImagesSmall[r] = ImageUtility.CreateStarRatingImage(r);
            m_StarRatingImagesSmall[r].Mutate(x => x.Resize(0, 25));
        }

        m_RelicTemplateImages = new Image<Rgba32>[6];
        for (var i = 1; i <= 5; i++)
        {
            var path = string.Format(FileNameFormat.Genshin.RelicTemplateName, i);
            m_RelicTemplateImages[i] = await Image.LoadAsync<Rgba32>(
                await ImageRepository.DownloadFileToStreamAsync(path, cancellationToken), cancellationToken);
            m_RelicTemplateImages[i].Mutate(x => x.Resize(new Size(0, 150), KnownResamplers.Bicubic, true));
        }

        m_RelicSlotTemplate = new Image<Rgba32>(970, 170, Color.Transparent.ToPixel<Rgba32>());
        m_RelicSlotTemplate.Mutate(ctx =>
        {
            ctx.Paint(canvas =>
            {
                _ = canvas.Save(ClipOptions, new RoundedRectanglePolygon(new RectangleF(0, 0, 970, 170), 15));
                canvas.Fill(Brushes.Solid(Color.FromPixel(new Rgba32(0, 0, 0, 0.25f))),
                    new Rectangle(0, 0, 970, 170));
                canvas.Restore();
            });
        });

        m_OverlayImage = await Image.LoadAsync<Rgba32>(
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

        if (Fonts.Medium == null || Fonts.Small == null)
            throw new CommandException("An error occurred when generating Genshin Character card");

        var characterPortrait = await LoadPortraitAsync(context,
            () => LoadImageFromRepositoryAsync<Rgba32>(
                charInfo.Base.ToImageName(), disposables, cancellationToken),
            disposables, cancellationToken);


        var offsetX = context.PortraitConfig?.OffsetX ?? 0;
        var scaledImageMinX = (1280 - characterPortrait.Width) / 2 + offsetX;
        var fadeStart = FadeX - scaledImageMinX;
        var fadeEnd = fadeStart + FadeWidth;
        characterPortrait.Mutate(ctx => ctx.ApplyGradientFade(fadeStart, fadeEnd, EasingType.InCubic));

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

        Task<(Relic? Data, Image<Rgba32>? Image)>[] relicImageTasks =
        [
            .. Enumerable.Range(0, 5).Select(async i =>
            {
                var relic = charInfo.Relics.FirstOrDefault(x => x.Pos == i + 1);
                if (relic != null)
                {
                    var relicImage = await LoadImageFromRepositoryAsync<Rgba32>(relic.ToImageName(), disposables, cancellationToken);
                    return (Data: relic, Image: relicImage);
                }
                return (Data: (Relic?)null, Image: (Image<Rgba32>?)null);
            })
        ];

        if (charInfo.Constellations?.FirstOrDefault(x => x.Pos == 3)?.IsActived ?? false)
            AssignConstEffects(charInfo.Constellations[2], charInfo.Skills);

        if (charInfo.Constellations?.FirstOrDefault(x => x.Pos == 5)?.IsActived ?? false)
            AssignConstEffects(charInfo.Constellations[4], charInfo.Skills);

        var weaponImage = await weaponImageTask;

        (bool Active, Image Image)[] constellationIcons = [.. await Task.WhenAll(constellationTasks)];

        (Skill Data, Image Image)[] skillIcons = [.. await Task.WhenAll(skillTasks)];

        (Relic? Data, Image<Rgba32>? Image)[] relicSlots = [.. await Task.WhenAll(relicImageTasks)];

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
            .. charInfo.SelectedProperties.Where(x => float.Parse(x.Final.TrimEnd('%'), CultureInfo.InvariantCulture) >
                                                          StatMappingUtility.GetDefaultValue(x.PropertyType!.Value,
                                                              Game.Genshin))
                    .OrderBy(x => x.PropertyType)
        ];

        StatProperty[] stats;
        if (bonusStats.Length >= 6)
            stats =
            [
                .. charInfo.BaseProperties.Take(4)
                        .Where(x => float.Parse(x.Final.TrimEnd('%'), CultureInfo.InvariantCulture) >
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

                _ = canvas.SaveLayer(new GraphicsOptions { ColorBlendingMode = PixelColorBlendingMode.Overlay });
                canvas.DrawImage(m_OverlayImage, m_OverlayImage.Bounds,
                    new RectangleF(0, 0, background.Width, background.Height), KnownResamplers.Bicubic);
                canvas.Restore();

                var textColor = Color.White;

                _ = canvas.SaveLayer();
                var offsetX = context.PortraitConfig?.OffsetX ?? 0;
                var offsetY = context.PortraitConfig?.OffsetY ?? 0;
                canvas.DrawImage(characterPortrait, characterPortrait.Bounds,
                    new RectangleF((1280 - characterPortrait.Width) / 2 + offsetX, 100 + (1080 - characterPortrait.Height) / 2 + offsetY,
                        characterPortrait.Width, characterPortrait.Height),
                    KnownResamplers.Bicubic);
                canvas.Restore();

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

                canvas.DrawAttribution(new RichTextOptions(Fonts.Tiny)
                {
                    Origin = new PointF(960, 1070),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    TextAlignment = TextAlignment.End,
                }, extraText: context.PortraitConfig?.ArtistAttribution != null ? $"Cre: {context.PortraitConfig.ArtistAttribution}" : null);

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
                var weaponStars = m_StarRatingImages[charInfo.Weapon.Rarity.GetValueOrDefault(1)];
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
                canvas.DrawRoundedRectangleOverlay(80 + (int)statSize.Width, 60, new PointF(1450, 230),
                    new RoundedRectangleOverlayStyle(Color.FromPixel(new Rgba32(0, 0, 0, 0.45f)), CornerRadius: 10));

                var statImage = m_StatImages.GetValueOrDefault(charInfo.Weapon.MainProperty.PropertyType!.Value);
                if (statImage != null)
                {
                    canvas.DrawImage(statImage,
                        statImage.Bounds,
                        new RectangleF(1455, 236, statImage.Width,
                            statImage.Height),
                        KnownResamplers.Bicubic);
                }
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new PointF(1514, 240)
                }, charInfo.Weapon.MainProperty.Final, Brushes.Solid(textColor), null);
                if (charInfo.Weapon.SubProperty != null)
                {
                    var substatSize =
                        TextMeasurer.MeasureBounds(charInfo.Weapon.SubProperty.Final, new TextOptions(Fonts.Normal));
                    canvas.DrawRoundedRectangleOverlay(80 + (int)substatSize.Width, 60, new PointF(1630, 230),
                        new RoundedRectangleOverlayStyle(Color.FromPixel(new Rgba32(0, 0, 0, 0.45f)), CornerRadius: 10));
                    var substatImage = m_StatImages.GetValueOrDefault(charInfo.Weapon.SubProperty.PropertyType!.Value);
                    if (substatImage != null)
                    {
                        canvas.DrawImage(substatImage,
                            substatImage.Bounds,
                            new RectangleF(1635, 236, substatImage.Width,
                                substatImage.Height),
                            KnownResamplers.Bicubic);
                    }
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
                            isBase && float.Parse(stat.Final.TrimEnd('%'), CultureInfo.InvariantCulture) > float.Parse(stat.Base.TrimEnd('%'), CultureInfo.InvariantCulture) ? $"+{stat.Add}" : null),
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

                for (var i = 0; i < relicSlots.Length; i++)
                {
                    var slot = relicSlots[i];
                    if (slot.Data != null)
                    {
                        DrawRelicSlotImage(canvas, slot.Data, slot.Image!, new Point(2200, 40 + i * 185));
                    }
                    else
                    {
                        DrawTemplateRelicSlotImage(canvas, new Point(2200, 40 + i * 185), i + 1);
                    }
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

    protected override Image<Rgba32> CreateBackground()
    {
        return new Image<Rgba32>(3240, 1080, Color.Transparent.ToPixel<Rgba32>());
    }

    private void DrawRelicSlotImage(DrawingCanvas canvas, Relic relic, Image relicImage, Point position)
    {
        relicImage.Mutate(x => x.Transform(new AffineTransformBuilder().AppendTranslation(new PointF(-40, -40))));

        using var region = canvas.CreateRegion(new Rectangle(position.X, position.Y, 970, 170));
        _ = region.Save(ClipOptions, new RoundedRectanglePolygon(new RectangleF(0, 0, 970, 170), 15));
        region.Fill(Brushes.Solid(Color.FromPixel(new Rgba32(0, 0, 0, 0.25f))), new Rectangle(0, 0, 970, 170));
        region.DrawImage(relicImage, relicImage.Bounds,
            new RectangleF(0, 0, relicImage.Width, relicImage.Height), KnownResamplers.Bicubic);
        region.Restore();

        var statImg = m_StatImages.GetValueOrDefault(relic.MainProperty.PropertyType!.Value);
        if (statImg != null)
        {
            region.DrawImage(statImg, statImg.Bounds,
                new RectangleF(280, 20, statImg.Width, statImg.Height), KnownResamplers.Bicubic);
        }

        region.DrawText(new RichTextOptions(Fonts.Normal)
        {
            TextAlignment = TextAlignment.End,
            HorizontalAlignment = HorizontalAlignment.Right,
            Origin = new Vector2(320, 70)
        }, relic.MainProperty.Value, Brushes.Solid(Color.White), null);
        region.DrawText(new RichTextOptions(Fonts.Small!)
        {
            TextAlignment = TextAlignment.End,
            HorizontalAlignment = HorizontalAlignment.Right,
            Origin = new Vector2(320, 130)
        }, $"+{relic.Level!.Value}", Brushes.Solid(Color.White), null);
        var stars = m_StarRatingImagesSmall[relic.Rarity.GetValueOrDefault(1)];
        region.DrawImage(stars, stars.Bounds,
            new RectangleF(120, 130, stars.Width, stars.Height), KnownResamplers.Bicubic);

        for (var i = 0; i < relic.SubPropertyList.Count; i++)
        {
            var subStat = relic.SubPropertyList[i];
            var xOffset = i % 2 * 290;
            var yOffset = i / 2 * 80;
            var color = Color.White;
            if (subStat.PropertyType is 2 or 5 or 8)
            {
                var dim = m_DimmedStatImages.GetValueOrDefault(subStat.PropertyType!.Value);
                if (dim != null)
                {
                    region.DrawImage(dim, dim.Bounds,
                        new RectangleF(375 + xOffset, 26 + yOffset, dim.Width, dim.Height), KnownResamplers.Bicubic);
                }
                color = Color.FromPixel(new Rgb24(128, 128, 128));
            }
            else
            {
                var subStatImage = m_StatImages.GetValueOrDefault(subStat.PropertyType!.Value);
                if (subStatImage != null)
                {
                    region.DrawImage(subStatImage, subStatImage.Bounds,
                        new RectangleF(375 + xOffset, 26 + yOffset, subStatImage.Width, subStatImage.Height), KnownResamplers.Bicubic);
                }
            }

            region.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new PointF(439 + xOffset, 30 + yOffset)
            }, subStat.Value, Brushes.Solid(color), null);

            var rolls = string.Concat(Enumerable.Repeat('.', subStat.Times.GetValueOrDefault(0) + 1));
            region.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new PointF(575 + xOffset, 15 + yOffset)
            }, rolls, Brushes.Solid(color), null);
        }
    }

    private void DrawTemplateRelicSlotImage(DrawingCanvas canvas, Point position, int slotIndex)
    {
        using var region = canvas.CreateRegion(new Rectangle(position.X, position.Y, 970, 170));
        _ = region.Save(ClipOptions, new RoundedRectanglePolygon(new RectangleF(0, 0, 970, 170), 15));
        region.Fill(Brushes.Solid(Color.FromPixel(new Rgba32(0, 0, 0, 0.25f))), new Rectangle(0, 0, 970, 170));

        var relicImage = m_RelicTemplateImages[slotIndex];
        region.DrawImage(relicImage, relicImage.Bounds,
            new RectangleF(25, 5, relicImage.Width, relicImage.Height), KnownResamplers.Bicubic);

        region.Restore();

        region.DrawText(new RichTextOptions(Fonts.Normal)
        {
            Origin = new Vector2(525, 95),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        }, "No Artifact", Brushes.Solid(Color.White), null);
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
