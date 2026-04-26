#region

using System.Numerics;
using Mehrak.Application.Renderers;
using Mehrak.Application.Renderers.Extensions;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Services.Hsr.Character;

public class HsrCharacterCardService : CardServiceBase<HsrCharacterInformation>
{
    private readonly IServiceScopeFactory m_ScopeFactory;
    private Dictionary<int, Image> m_StatImages = null!;
    private Dictionary<int, Image> m_TemplateRelicSlots = null!;
    private Image<Rgba32> m_RelicSlotTemplate = null!;

    private const string StatsPath = FileNameFormat.Hsr.StatsName;

    public HsrCharacterCardService(IImageRepository imageRepository,
        IServiceScopeFactory scopeFactory,
        ILogger<HsrCharacterCardService> logger,
        IApplicationMetrics metrics)
        : base(
            "Hsr Character",
            imageRepository,
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

        StaticBackground = await Image.LoadAsync<Rgba32>(
            await ImageRepository.DownloadFileToStreamAsync("hsr_bg", cancellationToken),
            cancellationToken);

        m_RelicSlotTemplate = new Image<Rgba32>(750, 150);
        m_RelicSlotTemplate.Mutate(x =>
        {
            x.Fill(new Rgba32(255, 255, 255, 0.1f));
            x.ApplyRoundedCorners(30);
        });

        m_TemplateRelicSlots = await Enumerable.Range(1, 6).ToAsyncEnumerable()
            .ToDictionaryAsync((x, token) => ValueTask.FromResult(x),
                async (x, token) => await CreateTemplateRelicSlotImageAsync(x, token), cancellationToken: cancellationToken);

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

        var characterPortraitTask = LoadImageFromRepositoryAsync<Rgba32>(
            characterInformation.ToImageName(), disposables, cancellationToken);
        var equipImageTask = characterInformation.Equip == null
            ? LoadImageFromRepositoryAsync<Rgba32>("hsr_lightcone_template", disposables, cancellationToken)
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

        Task<Image>[] relicImageTasks =
        [
            .. Enumerable.Range(0, 4).Select(async i =>
            {
                var relic = characterInformation.Relics!.FirstOrDefault(x => x.Pos == i + 1);
                if (relic != null)
                {
                    var relicImage = await CreateRelicSlotImageAsync(relic, cancellationToken);
                    disposables.Add(relicImage);
                    return relicImage;
                }
                else
                {
                    var templateRelicImage = m_TemplateRelicSlots[i + 1];
                    return templateRelicImage;
                }
            })
        ];

        Task<Image>[] ornamentImageTasks =
        [
            .. Enumerable.Range(0, 2).Select(async i =>
            {
                var ornament = characterInformation.Ornaments!.FirstOrDefault(x => x.Pos == i + 5);
                if (ornament != null)
                {
                    var ornamentImage = await CreateRelicSlotImageAsync(ornament, cancellationToken);
                    disposables.Add(ornamentImage);
                    return ornamentImage;
                }
                else
                {
                    var templateOrnamentImage = m_TemplateRelicSlots[i + 5];
                    return templateOrnamentImage;
                }
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

        var characterPortrait = await characterPortraitTask;
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
        Image[] relicImages = [.. await Task.WhenAll(relicImageTasks)];
        Image[] ornamentImages = [.. await Task.WhenAll(ornamentImageTasks)];
        (Skill Data, Image Image)[] servantImages = [.. await Task.WhenAll(servantTask)];

        background.Mutate(ctx =>
        {
            ctx.DrawImage(characterPortrait,
                new Point(400 - characterPortrait.Width / 2, 700 - characterPortrait.Height / 2), 1f);
        });

        var clone = background.CloneAs<Rgba32>();
        disposables.Add(clone);
        clone.Mutate(ctx =>
        {
            ctx.Crop(new Rectangle(800, 0, 2200, 1200));
            ctx.GaussianBlur(30);
        });

        Image<Rgba32> overlay = new(2500, 1200);
        disposables.Add(overlay);
        overlay.Mutate(ctx =>
        {
            ctx.DrawImage(clone, Point.Empty, 1f);
            ctx.ApplyRoundedCorners(100);
            ctx.Brightness(0.35f);
        });

        background.Mutate(ctx =>
        {
            ctx.DrawTextWithShadow(characterInformation.Name!, new RichTextOptions(Fonts.Title)
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

            ctx.DrawTextWithShadow($"Lv. {characterInformation.Level}", Fonts.Normal,
                new PointF(70, bounds.Bottom + 20), Color.White);
            ctx.DrawImage(overlay, new Point(800, 0), 1f);
            ctx.DrawText(context.GameProfile.GameUid, Fonts.Small, Color.White, new PointF(70, 1150));

            for (var i = 0; i < ranks.Length; i++)
            {
                var offset = i * 100;
                if (!ranks[i].Active) ranks[i].Image.Mutate(x => x.Brightness(0.5f));
                ctx.DrawCenteredIcon(ranks[i].Image, new PointF(900, 1115 - offset), 45, 5, Color.DarkSlateGray,
                    accentColor, 5f);
            }

            for (var i = 0; i < baseSkillImages.Length; i++)
            {
                var offset = i * 100;
                var skillColor = baseSkillImages[i].Data.Remake switch
                {
                    "Elation Skill" => Color.FromRgb(255, 176, 161),
                    _ => accentColor
                };
                ctx.DrawCenteredIcon(baseSkillImages[i].Image, new PointF(900, 80 + offset), 45, 5,
                    Color.DarkSlateGray, skillColor, 5f);

                ctx.DrawCenteredTextInEllipse(
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
                        ctx.DrawCenteredIcon(skill.Image, new PointF(1020 + xOffset, 80 + yOffset), 45, 5,
                            Color.DarkSlateGray, accentColor, 5f);
                    }
                    else
                    {
                        var xOffset = (j - 1) * 100;
                        ctx.DrawCenteredIcon(skill.Image, new PointF(1120 + xOffset, 80 + yOffset), 30, 5,
                            Color.DarkSlateGray, accentColor, 5f);
                    }
                }
            }

            var type4Skill = characterInformation.Skills.Count(x => x.PointType == 4);

            for (var i = 0; i < servantImages.Length; i++)
            {
                var offset = (i + type4Skill) * 120;
                ctx.DrawCenteredIcon(servantImages[i].Image, new PointF(900 + offset, 480), 45, 5,
                    Color.DarkSlateGray, accentColor, 5f);

                EllipsePolygon levelEllipse = new(new PointF(865 + offset, 515), 20);
                ctx.Fill(new SolidBrush(Color.LightSlateGray), levelEllipse);
                ctx.DrawText(new RichTextOptions(Fonts.Small)
                {
                    Origin = new PointF(864 + offset, 516),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }, servantImages[i].Data.Level!.ToString()!,
                    servantImages[i].Data.IsRankWork ? Color.Aqua : Color.White);
            }

            if (characterInformation.Equip != null)
            {
                ctx.DrawImage(equipImage, new Point(1000, 730), 1f);
                ctx.DrawText(new RichTextOptions(Fonts.Medium)
                {
                    Origin = new PointF(1000, 660),
                    WrappingLength = 300,
                    VerticalAlignment = VerticalAlignment.Bottom
                }, characterInformation.Equip.Name!, Color.White);
                ctx.DrawCenteredTextInEllipse(
                    ((char)(0x215F + characterInformation.Equip.Rank)).ToString(),
                    new PointF(1020, 690),
                    20,
                    new EllipseTextStyle(
                        Fonts.Small,
                        Color.Gold,
                        Color.DarkSlateGray,
                        Color.Gold,
                        2f));
                ctx.DrawText($"Lv. {characterInformation.Equip.Level}", Fonts.Normal, Color.White,
                    new PointF(1080, 670));
                using var stars = ImageUtility.GenerateFourSidedStarRating(characterInformation.Equip.Rarity,
                    false);
                ctx.DrawImage(stars, new Point(990, 730), 1f);
            }
            else
            {
                RectangleF rectangle = new(1000, 730, 300, 420);
                ctx.DrawImage(equipImage, new Point(1000, 805), 1f);
                ctx.Draw(Color.White, 5f, rectangle);
                ctx.DrawText(new RichTextOptions(Fonts.Medium)
                {
                    Origin = new PointF(1000, 710),
                    WrappingLength = 300,
                    VerticalAlignment = VerticalAlignment.Bottom
                }, "No Light Cone", Color.White);
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

                ctx.DrawStatLine(
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

            for (var i = 0; i < relicImages.Length; i++)
            {
                var offset = i * 170;
                ctx.DrawImage(relicImages[i], new Point(2200, 50 + offset), 1f);
            }

            var k = 0;
            foreach (var relicSet in activeRelicSet)
            {
                var offset = k * 30;
                ctx.DrawText(relicSet.Value.ToString(), Fonts.Small, Color.White,
                    new PointF(2200, 720 + offset));
                ctx.DrawText(int.TryParse(relicSet.Key, out _) ? $"Unknown Relic Set {k + 1}" : relicSet.Key,
                    Fonts.Small, Color.White, new PointF(2230, 720 + offset));
                k++;
            }

            for (var i = 0; i < ornamentImages.Length; i++)
            {
                var offset = i * 170;
                ctx.DrawImage(ornamentImages[ornamentImages.Length - 1 - i], new Point(2200, 1000 - offset), 1f);
            }

            k = 0;
            foreach (var ornamentSet in activeOrnamentSet)
            {
                var offset = k * 30;
                ctx.DrawText(new RichTextOptions(Fonts.Small)
                {
                    Origin = new PointF(2940, 820 - offset),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom
                }, ornamentSet.Value.ToString(), Color.White);
                ctx.DrawText(new RichTextOptions(Fonts.Small)
                {
                    Origin = new PointF(2910, 820 - offset),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom
                }, int.TryParse(ornamentSet.Key, out _) ? $"Unknown Ornament Set {k + 1}" : ornamentSet.Key, Color.White);
                k++;
            }
        });
    }

    private async Task<Image<Rgba32>> CreateRelicSlotImageAsync(Relic relic, CancellationToken cancellationToken = default)
    {
        await using var stream = await ImageRepository.DownloadFileToStreamAsync(relic.ToImageName(), cancellationToken);
        using var relicImage = await Image.LoadAsync<Rgba32>(stream, cancellationToken);
        var relicSlotImage = m_RelicSlotTemplate.Clone();
        relicSlotImage.Mutate(ctx =>
        {
            ctx.DrawImage(relicImage, new Point(10, 0), 1f);
            using var stars = ImageUtility.GenerateFourSidedStarRating(relic.Rarity);
            ctx.DrawImage(stars, new Point(20, 115), 1f);
            ctx.DrawImage(m_StatImages[relic.MainProperty!.PropertyType!.Value], new Point(125, 10), 1f);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new PointF(230, 60),
                HorizontalAlignment = HorizontalAlignment.Right
            }, relic.MainProperty!.Value!, Color.White);
            ctx.DrawText($"+{relic.Level}", Fonts.Small, Color.White, new PointF(180, 20));

            for (var i = 0; i < relic.Properties!.Count; i++)
            {
                var subStat = relic.Properties[i];
                var subStatImage = m_StatImages[subStat.PropertyType!.Value];
                var xOffset = i % 2 * 245;
                var yOffset = i / 2 * 70;
                var color = Color.White;
                if (subStat.PropertyType is 27 or 29 or 31)
                {
                    using var dim = subStatImage.Clone(x => x.Brightness(0.5f));
                    ctx.DrawImage(dim, new Point(260 + xOffset, 15 + yOffset), 1f);
                    color = Color.FromRgb(128, 128, 128);
                }
                else
                {
                    ctx.DrawImage(subStatImage, new Point(260 + xOffset, 15 + yOffset), 1f);
                }

                ctx.DrawText(subStat.Value!, Fonts.Normal, color, new PointF(310 + xOffset, 20 + yOffset));
                var rolls = string.Concat(Enumerable.Repeat('.', subStat.Times.GetValueOrDefault(0)));
                ctx.DrawText(rolls, Fonts.Normal, color, new PointF(435 + xOffset, 10 + yOffset));
            }
        });
        return relicSlotImage;
    }

    private async Task<Image> CreateTemplateRelicSlotImageAsync(int slot, CancellationToken cancellationToken = default)
    {
        var path = $"hsr_relic_template_{slot}";
        Logger.LogDebug("Loading template relic image from {Path}", path);

        await using var stream = await ImageRepository.DownloadFileToStreamAsync(path, cancellationToken);
        var relicImage = await Image.LoadAsync<Rgba32>(stream, cancellationToken);
        var template = m_RelicSlotTemplate.Clone();
        template.Mutate(ctx =>
        {
            ctx.DrawImage(relicImage, new Point(25, 10), 1f);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(425, 75),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            }, "No Relic", Color.White);

            relicImage.Dispose();
        });

        return template;
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
