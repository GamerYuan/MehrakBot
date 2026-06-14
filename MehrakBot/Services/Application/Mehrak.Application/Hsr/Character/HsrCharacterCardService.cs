#region

using System.Numerics;
using Mehrak.Application.Renderers.Extensions;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Renderers;
using Mehrak.Application.Shared.Renderers.Extensions;
using Mehrak.Application.Shared.Utility;
using Mehrak.Domain.Character;
using Mehrak.Domain.Character.Models;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.User.Abstractions;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.Infrastructure.Relic;
using Microsoft.EntityFrameworkCore;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Hsr.Character;

public class HsrCharacterCardService : CharacterCardServiceBase<HsrCharacterInformation>
{
    private readonly IServiceScopeFactory m_ScopeFactory;
    private Dictionary<int, Image> m_StatImages = null!;
    private Dictionary<int, Image> m_DimmedStatImages = null!;
    private Image<Rgba32>[] m_RelicTemplateImages = new Image<Rgba32>[7];
    private Image<Rgba32>[] m_EquipStarImages = new Image<Rgba32>[5];
    private Image<Rgba32>[] m_RelicStarImages = new Image<Rgba32>[5];

    private const string StatsPath = FileNameFormat.Hsr.StatsName;

    public HsrCharacterCardService(IImageRepository imageRepository,
        IUserPortraitService userPortraitService,
        IServiceScopeFactory scopeFactory,
        ILogger<HsrCharacterCardService> logger,
        IApplicationMetrics metrics)
        : base(
            "Hsr Character",
            imageRepository,
            userPortraitService,
            logger,
            metrics,
            LoadFonts("Assets/Fonts/hsr.ttf", titleSize: 64, normalSize: 40, mediumSize: 36, smallSize: 28))
    {
        m_ScopeFactory = scopeFactory;
    }

    public override async Task LoadStaticResourcesAsync(CancellationToken cancellationToken = default)
    {
        m_StatImages = await StatMappingUtility.HsrMapping.Keys.ToAsyncEnumerable().Where(x => x != 8).Select(async (x, token) =>
        {
            var path = string.Format(StatsPath, x);
            var image = await Image.LoadAsync(await ImageRepository.DownloadFileToStreamAsync(path), token);
            return new KeyValuePair<int, Image>(x, image);
        }).ToDictionaryAsync(x => x.Key, x => x.Value, cancellationToken: cancellationToken);

        m_DimmedStatImages = m_StatImages.ToDictionary(x => x.Key, x => x.Value.Clone(ctx => ctx.Brightness(0.5f)));

        StaticBackground = await Image.LoadAsync<Rgba32>(
            await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hsr.BackgroundName, cancellationToken),
            cancellationToken);

        for (var i = 1; i <= 6; i++)
        {
            var path = string.Format(FileNameFormat.Hsr.RelicTemplateName, i);
            m_RelicTemplateImages[i] = await Image.LoadAsync<Rgba32>(
                await ImageRepository.DownloadFileToStreamAsync(path, cancellationToken), cancellationToken);
        }

        for (var i = 1; i <= 5; i++)
        {
            m_EquipStarImages[i - 1] = ImageUtility.CreateFourSidedStarRatingImage(i, false);
            m_RelicStarImages[i - 1] = ImageUtility.CreateFourSidedStarRatingImage(i, true);
        }

        Logger.LogInformation("Resources initialized successfully with {Count} icons.", m_StatImages.Count);
    }

    public override async Task RenderCardAsync(
        Image<Rgba32> background,
        ICardGenerationContext<HsrCharacterInformation> context,
        DisposableBag disposables,
        CancellationToken cancellationToken = default)
    {
        var characterInformation = context.Data;

        using var scope = m_ScopeFactory.CreateScope();
        var relicContext = scope.ServiceProvider.GetRequiredService<RelicDbContext>();

        var userPortrait = await TryLoadUserPortraitAsync(
            context.UserId, Game.HonkaiStarRail, characterInformation.Name,
            disposables, cancellationToken);

        Image<Rgba32> characterPortrait;
        CharacterPortraitConfig? portraitConfig;
        if (userPortrait != null)
        {
            characterPortrait = userPortrait.Image;
            portraitConfig = userPortrait.Config;
        }
        else
        {
            characterPortrait = await LoadImageFromRepositoryAsync<Rgba32>(
                characterInformation.ToImageName(), disposables, cancellationToken);
            portraitConfig = context.GetParameter<CharacterPortraitConfig>("portraitConfig");
        }

        var equipImageTask = characterInformation.Equip == null
            ? LoadImageFromRepositoryAsync<Rgba32>(FileNameFormat.Hsr.LightconeTemplateName, disposables, cancellationToken)
            : LoadImageFromRepositoryAsync<Rgba32>(
                characterInformation.Equip.ToImageName(), disposables, cancellationToken);
        Task<(bool Active, Image Image)>[] rankTasks =
        [
            .. characterInformation.Ranks!.Select(async x =>
            {
                var image = await LoadImageFromRepositoryAsync(x.ToImageName(), disposables, cancellationToken);
                return (Active: x.IsUnlocked, Image: image);
            })
        ];

        Skill[] baseSkill =
            [.. characterInformation.Skills!.Where(x => (x.PointType == 2 && x.Remake != "Technique") || x.PointType == 4)];
        var skillChains =
            BuildSkillTree([.. characterInformation.Skills!.Where(x => x.PointType != 2 && x.PointType != 4)]);

        Task<(Skill Data, Image Image)>[] baseSkillTasks =
        [
            .. baseSkill.Select(async x =>
            {
                var image = await LoadImageFromRepositoryAsync(x.ToImageName(), disposables, cancellationToken);
                return (Data: x, Image: image);
            })
        ];
        Task<(Skill Data, Image Image)[]>[] skillTasks =
        [
            .. skillChains.Select(async chain =>
            {
                var chainImages = await Task.WhenAll(chain.Select(async x =>
                {
                    var image = await LoadImageFromRepositoryAsync(x.ToImageName(), disposables, cancellationToken);
                    return (Data: x, Image: image);
                }));
                return chainImages;
            })
        ];

        Task<(Relic? Data, Image<Rgba32>? Image)>[] relicImageTasks =
        [
            .. Enumerable.Range(0, 4).Select(async i =>
            {
                var relic = characterInformation.Relics!.FirstOrDefault(x => x.Pos == i + 1);
                if (relic != null)
                {
                    var relicImage = await LoadImageFromRepositoryAsync<Rgba32>(relic.ToImageName(), disposables, cancellationToken);
                    return (Data: relic, Image: relicImage);
                }
                return (Data: (Relic?)null, Image: (Image<Rgba32>?)null);
            })
        ];

        Task<(Relic? Data, Image<Rgba32>? Image)>[] ornamentImageTasks =
        [
            .. Enumerable.Range(0, 2).Select(async i =>
            {
                var ornament = characterInformation.Ornaments!.FirstOrDefault(x => x.Pos == i + 5);
                if (ornament != null)
                {
                    var ornamentImage = await LoadImageFromRepositoryAsync<Rgba32>(ornament.ToImageName(), disposables, cancellationToken);
                    return (Data: ornament, Image: ornamentImage);
                }
                return (Data: (Relic?)null, Image: (Image<Rgba32>?)null);
            })
        ];

        Dictionary<string, int> activeRelicSet = [];
        foreach (var setId in characterInformation.Relics!.Select(x => x.GetSetId()))
        {
            var setName = await relicContext.HsrRelics.AsNoTracking()
                .Where(x => x.SetId == setId)
                .Select(x => x.SetName)
                .FirstOrDefaultAsync(cancellationToken);

            if (string.IsNullOrEmpty(setName)) setName = setId.ToString();

            if (!activeRelicSet.TryAdd(setName, 1))
                activeRelicSet[setName]++;
        }

        activeRelicSet = activeRelicSet
            .Where(x => x.Value >= 2)
            .ToDictionary(x => x.Key, x => x.Value);

        Dictionary<string, int> activeOrnamentSet = [];
        foreach (var setId in characterInformation.Ornaments!.Select(x => x.GetSetId()))
        {
            var setName = await relicContext.HsrRelics.AsNoTracking()
                .Where(x => x.SetId == setId)
                .Select(x => x.SetName)
                .FirstOrDefaultAsync(cancellationToken);

            if (string.IsNullOrEmpty(setName)) setName = setId.ToString();

            if (!activeOrnamentSet.TryAdd(setName, 1))
                activeOrnamentSet[setName]++;
        }

        activeOrnamentSet = activeOrnamentSet
            .Where(x => x.Value >= 2)
            .ToDictionary(x => x.Key, x => x.Value);

        Task<(Skill Data, Image Image)>[] servantTask =
        [
            .. characterInformation.ServantDetail!.ServantSkills!.Select(async x =>
            {
                var image = await LoadImageFromRepositoryAsync(x.ToImageName(), disposables, cancellationToken);
                return (Data: x, Image: image);
            })
        ];

        var accentColor = GetAccentColor(characterInformation.Element!);

        characterPortrait.Mutate(ctx =>
        {
            if (portraitConfig?.TargetScale > 0f)
            {
                var scale = portraitConfig.TargetScale.Value;
                ctx.Resize((int)(ctx.GetCurrentSize().Width * scale), 0, KnownResamplers.Lanczos3);
            }
            else
            {
                ctx.Resize(1000, 0, KnownResamplers.Lanczos3);
            }

            if (portraitConfig?.EnableGradientFade == true &&
                (portraitConfig?.GradientFadeStart ?? 0.75f) > 0f)
                ctx.ApplyGradientFade(portraitConfig?.GradientFadeStart ?? 0.75f);
        });

        var equipImage = await equipImageTask;
        (bool Active, Image Image)[] ranks = [.. (await Task.WhenAll(rankTasks)).Reverse()];
        (Skill Data, Image Image)[] baseSkillImages = [.. await Task.WhenAll(baseSkillTasks)];
        (Skill Data, Image Image)[][] skillImages = [.. await Task.WhenAll(skillTasks)];
        var stats = characterInformation.Properties!.Where(x =>
            float.Parse(x.Final!.TrimEnd('%')) >
            StatMappingUtility.GetDefaultValue(x.PropertyType!.Value, Game.HonkaiStarRail)).ToList();
        if (stats.Count < 7)
            stats =
            [
                .. stats.Concat(characterInformation.Properties!)
                    .DistinctBy(x => x.PropertyType!.Value).Take(7).OrderBy(x => x.PropertyType!.Value)
            ];
        (Relic? Data, Image<Rgba32>? Image)[] relicSlots = [.. await Task.WhenAll(relicImageTasks)];
        (Relic? Data, Image<Rgba32>? Image)[] ornamentSlots = [.. await Task.WhenAll(ornamentImageTasks)];
        (Skill Data, Image Image)[] servantImages = [.. await Task.WhenAll(servantTask)];

        background.Mutate(ctx =>
        {
            ctx.Paint(canvas =>
            {
                var offsetX = portraitConfig?.OffsetX ?? 0;
                var offsetY = portraitConfig?.OffsetY ?? 0;

                canvas.DrawImage(characterPortrait, characterPortrait.Bounds,
                    new RectangleF(400 - characterPortrait.Width / 2 + offsetX, 700 - characterPortrait.Height / 2 + offsetY,
                        characterPortrait.Width, characterPortrait.Height),
                    KnownResamplers.Bicubic);
                canvas.Apply(
                    CreateLeftRoundedRectanglePath(background.Width - 800, background.Height, 100).Translate(800, 0),
                    region =>
                    {
                        region.GaussianBlur(30);
                        region.Brightness(0.35f);
                    });

                canvas.DrawTextWithShadow(characterInformation.Name!, new RichTextOptions(Fonts.Title)
                {
                    Origin = new PointF(70, 50),
                    WrappingLength = 700,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                }, Color.White);

                var bounds = TextMeasurer.MeasureBounds(characterInformation.Name!,
                    new RichTextOptions(Fonts.Title)
                    {
                        Origin = new PointF(70, 50),
                        WrappingLength = 700,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top
                    });

                canvas.DrawTextWithShadow($"Lv. {characterInformation.Level}", Fonts.Normal,
                    new PointF(70, bounds.Bottom + 20), Color.White);

                canvas.DrawTextWithShadow(context.GameProfile.Nickname, Fonts.Normal, new PointF(70, 1110), Color.White);
                canvas.DrawTextWithShadow(context.GameProfile.GameUid, Fonts.Small, new PointF(70, 1150), Color.White);

                canvas.DrawAttribution(new RichTextOptions(Fonts.Tiny)
                {
                    Origin = new PointF(790, 1180),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    TextAlignment = TextAlignment.End,
                });

                for (var i = 0; i < ranks.Length; i++)
                {
                    var offset = i * 100;
                    if (!ranks[i].Active) ranks[i].Image.Mutate(x => x.Brightness(0.5f));
                    canvas.DrawCenteredIcon(ranks[i].Image, new PointF(900, 1115 - offset), 45, 5, Color.DarkSlateGray,
                        accentColor, 5f);
                }

                for (var i = 0; i < baseSkillImages.Length; i++)
                {
                    var offset = i * 100;
                    var skillColor = baseSkillImages[i].Data.Remake switch
                    {
                        "Elation Skill" => Color.FromPixel(new Rgb24(255, 176, 161)),
                        _ => accentColor
                    };
                    canvas.DrawCenteredIcon(baseSkillImages[i].Image, new PointF(900, 80 + offset), 45, 5,
                        Color.DarkSlateGray, skillColor, 5f);

                    canvas.DrawCenteredTextInEllipse(
                        baseSkillImages[i].Data.Level!.ToString()!,
                        new PointF(865, 115 + offset),
                        20,
                        new EllipseTextStyle(
                            Fonts.Small,
                            baseSkillImages[i].Data.IsRankWork ? Color.Aqua : Color.White,
                            Color.LightSlateGray));
                }

                for (var i = 0; i < skillImages.Length; i++)
                {
                    var yOffset = i * 100;
                    for (var j = 0; j < skillImages[i].Length; j++)
                    {
                        var skill = skillImages[i][j];
                        if (!skill.Data.IsActivated)
                            skill.Image.Mutate(x => x.Brightness(0.5f));

                        if (skill.Data.PointType == 3)
                        {
                            var xOffset = j * 100;
                            canvas.DrawCenteredIcon(skill.Image, new PointF(1020 + xOffset, 80 + yOffset), 45, 5,
                                Color.DarkSlateGray, accentColor, 5f);
                        }
                        else
                        {
                            var xOffset = (j - 1) * 100;
                            canvas.DrawCenteredIcon(skill.Image, new PointF(1120 + xOffset, 80 + yOffset), 30, 5,
                                Color.DarkSlateGray, accentColor, 5f);
                        }
                    }
                }

                var type4Skill = characterInformation.Skills.Count(x => x.PointType == 4);

                for (var i = 0; i < servantImages.Length; i++)
                {
                    var offset = (i + type4Skill) * 120;
                    canvas.DrawCenteredIcon(servantImages[i].Image, new PointF(900 + offset, 480), 45, 5,
                        Color.DarkSlateGray, accentColor, 5f);

                    EllipsePolygon levelEllipse = new(new PointF(865 + offset, 515), 20);
                    canvas.Fill(Brushes.Solid(Color.LightSlateGray), levelEllipse);
                    canvas.DrawText(new RichTextOptions(Fonts.Small)
                    {
                        Origin = new PointF(864 + offset, 516),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }, servantImages[i].Data.Level!.ToString()!,
                        Brushes.Solid(servantImages[i].Data.IsRankWork ? Color.Aqua : Color.White), null);
                }

                if (characterInformation.Equip != null)
                {
                    canvas.DrawImage(equipImage, equipImage.Bounds,
                        new RectangleF(1000, 730, equipImage.Width, equipImage.Height), KnownResamplers.Bicubic);
                    canvas.DrawText(new RichTextOptions(Fonts.Medium)
                    {
                        Origin = new PointF(1000, 660),
                        WrappingLength = 300,
                        VerticalAlignment = VerticalAlignment.Bottom
                    }, characterInformation.Equip.Name!, Brushes.Solid(Color.White), null);
                    canvas.DrawCenteredTextInEllipse(
                        ((char)(0x215F + characterInformation.Equip.Rank)).ToString(),
                        new PointF(1020, 690),
                        20,
                        new EllipseTextStyle(
                            Fonts.Small,
                            Color.Gold,
                            Color.DarkSlateGray,
                            Color.Gold,
                            2f));
                    canvas.DrawText(new RichTextOptions(Fonts.Normal)
                    {
                        Origin = new PointF(1080, 670)
                    }, $"Lv. {characterInformation.Equip.Level}", Brushes.Solid(Color.White), null);
                    var stars = m_EquipStarImages[characterInformation.Equip.Rarity - 1];
                    canvas.DrawImage(stars, stars.Bounds,
                        new RectangleF(990, 730, stars.Width, stars.Height), KnownResamplers.Bicubic);
                }
                else
                {
                    Rectangle rectangle = new(1000, 730, 300, 420);
                    canvas.DrawImage(equipImage, equipImage.Bounds,
                        new RectangleF(1000, 805, equipImage.Width, equipImage.Height), KnownResamplers.Bicubic);
                    canvas.Draw(Pens.Solid(Color.White, 5f), rectangle);
                    canvas.DrawText(new RichTextOptions(Fonts.Medium)
                    {
                        Origin = new PointF(1000, 710),
                        WrappingLength = 300,
                        VerticalAlignment = VerticalAlignment.Bottom
                    }, "No Light Cone", Brushes.Solid(Color.White), null);
                }

                var statOffset = 1100 / stats.Count;
                for (var i = 0; i < stats.Count; i++)
                {
                    var offset = i * statOffset;
                    var property = stats[i];
                    var statImage = m_StatImages.GetValueOrDefault(property.PropertyType!.Value);

                    if (statImage == null)
                    {
                        Logger.LogWarning("Stat image not found for property type {PropertyType}",
                            property.PropertyType);
                    }

                    canvas.DrawStatLine(
                        new StatLineData(
                            StatMappingUtility.HsrMapping[property.PropertyType!.Value],
                            property.Final!),
                        new StatLineStyle(
                            statImage,
                            Fonts.Normal,
                            Color.White),
                        new PointF(1400, 75 + offset),
                        740);
                }

                for (var i = 0; i < relicSlots.Length; i++)
                {
                    var slot = relicSlots[i];
                    if (slot.Data != null)
                    {
                        DrawRelicSlotImage(canvas, slot.Data, slot.Image!, new Point(2200, 50 + i * 170));
                    }
                    else
                    {
                        DrawTemplateRelicSlotImage(canvas, new Point(2200, 50 + i * 170), i + 1);
                    }
                }

                var k = 0;
                foreach (var relicSet in activeRelicSet)
                {
                    var offset = k * 30;
                    canvas.DrawText(new RichTextOptions(Fonts.Small)
                    {
                        Origin = new PointF(2200, 720 + offset)
                    }, relicSet.Value.ToString(), Brushes.Solid(Color.White), null);
                    canvas.DrawText(new RichTextOptions(Fonts.Small)
                    {
                        Origin = new PointF(2230, 720 + offset)
                    }, int.TryParse(relicSet.Key, out _) ? $"Unknown Relic Set {k + 1}" : relicSet.Key,
                        Brushes.Solid(Color.White), null);
                    k++;
                }

                for (var i = 0; i < ornamentSlots.Length; i++)
                {
                    var slot = ornamentSlots[ornamentSlots.Length - 1 - i];
                    var offset = i * 170;
                    if (slot.Data != null)
                    {
                        DrawRelicSlotImage(canvas, slot.Data, slot.Image!, new Point(2200, 1000 - offset));
                    }
                    else
                    {
                        DrawTemplateRelicSlotImage(canvas, new Point(2200, 1000 - offset), ornamentSlots.Length + 4 - i);
                    }
                }

                k = 0;
                foreach (var ornamentSet in activeOrnamentSet)
                {
                    var offset = k * 30;
                    canvas.DrawText(new RichTextOptions(Fonts.Small)
                    {
                        Origin = new PointF(2940, 820 - offset),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Bottom
                    }, ornamentSet.Value.ToString(), Brushes.Solid(Color.White), null);
                    canvas.DrawText(new RichTextOptions(Fonts.Small)
                    {
                        Origin = new PointF(2910, 820 - offset),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Bottom
                    }, int.TryParse(ornamentSet.Key, out _) ? $"Unknown Ornament Set {k + 1}" : ornamentSet.Key,
                        Brushes.Solid(Color.White), null);
                    k++;
                }
            });
        });
    }

    private void DrawRelicSlotImage(DrawingCanvas canvas, Relic relic, Image<Rgba32> relicImage, Point position)
    {
        using var region = canvas.CreateRegion(new Rectangle(position.X, position.Y, 750, 150));
        _ = region.Save(ClipOptions, new RoundedRectanglePolygon(new RectangleF(0, 0, 750, 150), 30));
        region.Fill(Brushes.Solid(Color.FromPixel(new Rgba32(255, 255, 255, 0.1f))), new Rectangle(0, 0, 750, 150));
        region.DrawImage(relicImage, relicImage.Bounds,
            new RectangleF(10, 0, relicImage.Width, relicImage.Height), KnownResamplers.Bicubic);
        region.Restore();

        var stars = m_RelicStarImages[relic.Rarity - 1];
        region.DrawImage(stars, stars.Bounds,
            new RectangleF(20, 115, stars.Width, stars.Height), KnownResamplers.Bicubic);
        var statImg = m_StatImages[relic.MainProperty!.PropertyType!.Value];
        region.DrawImage(statImg, statImg.Bounds,
            new RectangleF(125, 10, statImg.Width, statImg.Height), KnownResamplers.Bicubic);
        region.DrawText(new RichTextOptions(Fonts.Normal)
        {
            Origin = new PointF(230, 60),
            HorizontalAlignment = HorizontalAlignment.Right
        }, relic.MainProperty!.Value!, Brushes.Solid(Color.White), null);
        region.DrawText(new RichTextOptions(Fonts.Small)
        {
            Origin = new PointF(180, 20)
        }, $"+{relic.Level}", Brushes.Solid(Color.White), null);

        for (var i = 0; i < relic.Properties!.Count; i++)
        {
            var subStat = relic.Properties[i];
            var subStatImage = m_StatImages[subStat.PropertyType!.Value];
            var xOffset = i % 2 * 245;
            var yOffset = i / 2 * 70;
            var color = Color.White;
            if (subStat.PropertyType is 27 or 29 or 31)
            {
                var dim = m_DimmedStatImages[subStat.PropertyType!.Value];
                region.DrawImage(dim, dim.Bounds,
                    new RectangleF(260 + xOffset, 15 + yOffset, dim.Width, dim.Height), KnownResamplers.Bicubic);
                color = Color.FromPixel(new Rgb24(128, 128, 128));
            }
            else
            {
                region.DrawImage(subStatImage, subStatImage.Bounds,
                    new RectangleF(260 + xOffset, 15 + yOffset, subStatImage.Width, subStatImage.Height),
                    KnownResamplers.Bicubic);
            }

            region.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new PointF(310 + xOffset, 20 + yOffset)
            }, subStat.Value!, Brushes.Solid(color), null);
            var rolls = string.Concat(Enumerable.Repeat('.', subStat.Times.GetValueOrDefault(0)));
            region.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new PointF(435 + xOffset, 10 + yOffset)
            }, rolls, Brushes.Solid(color), null);
        }
    }

    private void DrawTemplateRelicSlotImage(DrawingCanvas canvas, Point position, int slotIndex)
    {
        using var region = canvas.CreateRegion(new Rectangle(position.X, position.Y, 750, 150));
        _ = region.Save(ClipOptions, new RoundedRectanglePolygon(new RectangleF(0, 0, 750, 150), 30));
        region.Fill(Brushes.Solid(Color.FromPixel(new Rgba32(255, 255, 255, 0.1f))), new Rectangle(0, 0, 750, 150));

        var relicImage = m_RelicTemplateImages[slotIndex];
        region.DrawImage(relicImage, relicImage.Bounds,
            new RectangleF(25, 10, relicImage.Width, relicImage.Height), KnownResamplers.Bicubic);

        region.Restore();

        region.DrawText(new RichTextOptions(Fonts.Normal)
        {
            Origin = new Vector2(425, 75),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        }, "No Relic", Brushes.Solid(Color.White), null);
    }

    private static List<List<Skill>> BuildSkillTree(List<Skill> skills)
    {
        List<List<Skill>> result = [];
        HashSet<string> processed = [];

        // Find all skills that have point_type == 3 and can be roots
        List<Skill> type3Skills = [.. skills.Where(s => s.PointType == 3)];

        // Build chains from each root
        foreach (var rootSkill in type3Skills)
        {
            if (processed.Contains(rootSkill.PointId!))
                continue;

            var chain = BuildChainFromRoot(rootSkill, processed, skills);
            if (chain.Count > 0) result.Add(chain);
        }

        // Collect any remaining unprocessed skills
        List<Skill> unprocessedSkills =
        [
            .. skills
                .Where(skill => !processed.Contains(skill.PointId!))
                .OrderBy(skill => skill.Anchor)
        ];

        // If there are unprocessed skills, add them as the first chain
        if (unprocessedSkills.Count > 0) result.Insert(0, unprocessedSkills);

        return result;
    }

    private static List<Skill> BuildChainFromRoot(Skill rootSkill, HashSet<string> processed, List<Skill> allSkills)
    {
        List<Skill> chain = [];
        Queue<Skill> queue = new();
        queue.Enqueue(rootSkill);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (processed.Contains(current.PointId!))
                continue;

            chain.Add(current);
            processed.Add(current.PointId!);

            // Find all skills that have this skill as their pre_point
            List<Skill> childSkills =
                [.. allSkills.Where(s => s.PrePoint == current.PointId && !processed.Contains(s.PointId!))];

            foreach (var childSkill in childSkills) queue.Enqueue(childSkill);
        }

        return chain;
    }

    private static Polygon CreateLeftRoundedRectanglePath(int width, int height, float radius)
    {
        float left = 0;
        float top = 0;
        float right = width;
        float bottom = height;

        var radiusX = radius;
        var radiusY = radius;

        if (radiusX <= 0 || radiusY <= 0)
        {
            return new Polygon(new LinearLineSegment(
                new PointF(left, top),
                new PointF(right, top),
                new PointF(right, bottom),
                new PointF(left, bottom)));
        }

        var radiusScale = MathF.Min(width / (radiusX + radiusX), height / (radiusY + radiusY));
        if (radiusScale < 1F)
        {
            radiusX *= radiusScale;
            radiusY *= radiusScale;
        }

        SizeF cornerRadius = new(radiusX, radiusY);
        PointF topLeftCenter = new(left + radiusX, top + radiusY);
        PointF bottomLeftCenter = new(left + radiusX, bottom - radiusY);

        return new Polygon(
            new ArcLineSegment(topLeftCenter, cornerRadius, 0F, 180F, 90F),
            new LinearLineSegment(new PointF(left + radiusX, top), new PointF(right, top)),
            new LinearLineSegment(new PointF(right, top), new PointF(right, bottom)),
            new LinearLineSegment(new PointF(right, bottom), new PointF(left + radiusX, bottom)),
            new ArcLineSegment(bottomLeftCenter, cornerRadius, 0F, 90F, 90F),
            new LinearLineSegment(new PointF(left, bottom - radiusY), new PointF(left, top + radiusY)));
    }

    private static Color GetAccentColor(string element)
    {
        return element switch
        {
            _ when element.Equals("Physical", StringComparison.OrdinalIgnoreCase) => Color.ParseHex("#acabab"),
            _ when element.Equals("Fire", StringComparison.OrdinalIgnoreCase) => Color.ParseHex("#e83e3e"),
            _ when element.Equals("Ice", StringComparison.OrdinalIgnoreCase) => Color.ParseHex("#1fb6d1"),
            _ when element.Equals("Lightning", StringComparison.OrdinalIgnoreCase) => Color.ParseHex("#bb4cd3"),
            _ when element.Equals("Wind", StringComparison.OrdinalIgnoreCase) => Color.ParseHex("#3cc088"),
            _ when element.Equals("Quantum", StringComparison.OrdinalIgnoreCase) => Color.ParseHex("#5058e0"),
            _ when element.Equals("Imaginary", StringComparison.OrdinalIgnoreCase) => Color.ParseHex("#d6c146"),
            _ => Color.DarkBlue
        };
    }
}
