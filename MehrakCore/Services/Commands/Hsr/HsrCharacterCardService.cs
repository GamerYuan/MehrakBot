#region

using System.Numerics;
using MehrakCore.ApiResponseTypes.Hsr;
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

namespace MehrakCore.Services.Commands.Hsr;

public class HsrCharacterCardService : ICharacterCardService<HsrCharacterInformation>
{
    private readonly ImageRepository m_ImageRepository;
    private readonly HsrImageUpdaterService m_ImageUpdater;
    private readonly ILogger<HsrCharacterCardService> m_Logger;

    private readonly Dictionary<int, Image> m_StatImages;

    private const string BasePath = "hsr_{0}";
    private const string StatsPath = "hsr_stats_{0}";

    private readonly Font m_SmallFont;
    private readonly Font m_NormalFont;
    private readonly Font m_MediumFont;
    private readonly Font m_TitleFont;

    private readonly JpegEncoder m_JpegEncoder;
    private readonly Image<Rgba32> m_RelicSlotTemplate;

    public HsrCharacterCardService(ImageRepository imageRepository,
        ImageUpdaterService<HsrCharacterInformation> imageUpdater,
        ILogger<HsrCharacterCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_ImageUpdater = (HsrImageUpdaterService)imageUpdater;
        m_Logger = logger;

        var fontFamily = new FontCollection().Add("Fonts/hsr.ttf");

        m_TitleFont = fontFamily.CreateFont(64);
        m_NormalFont = fontFamily.CreateFont(40);
        m_MediumFont = fontFamily.CreateFont(36);
        m_SmallFont = fontFamily.CreateFont(28);

        m_JpegEncoder = new JpegEncoder
        {
            Quality = 90,
            Interleaved = false
        };

        int[] statIds =
        [
            1, 2, 3, 4, 5, 6, 7, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30,
            31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60
        ];

        m_StatImages = statIds.ToAsyncEnumerable().Where(x => x != 8).SelectAwait(async x =>
        {
            var path = string.Format(StatsPath, x);
            m_Logger.LogTrace("Downloading stat icon {StatId}: {Path}", x, path);
            var image = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(path));
            // image.Mutate(ctx => ctx.Resize(new Size(48, 0), KnownResamplers.Bicubic, true));
            return new KeyValuePair<int, Image>(x, image);
        }).ToBlockingEnumerable().ToDictionary(x => x.Key, x => x.Value);

        m_RelicSlotTemplate = new Image<Rgba32>(750, 150);
        m_RelicSlotTemplate.Mutate(x =>
        {
            x.Fill(new Rgba32(255, 255, 255, 0.1f));
            x.ApplyRoundedCorners(30);
        });

        m_Logger.LogInformation("Resources initialized successfully with {Count} icons.", m_StatImages.Count);

        m_Logger.LogInformation("HsrCharacterCardService initialized");
    }

    public async Task<Stream> GenerateCharacterCardAsync(HsrCharacterInformation characterInformation, string gameUid)
    {
        ArgumentNullException.ThrowIfNull(characterInformation);
        ArgumentNullException.ThrowIfNull(gameUid);

        m_Logger.LogInformation("Generating character card for {CharacterName}", characterInformation.Name);

        List<IDisposable> disposableResources = [];

        try
        {
            var backgroundTask = Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("hsr_bg"));

            var characterPortraitTask = Image.LoadAsync<Rgba32>(await m_ImageRepository.DownloadFileToStreamAsync(
                string.Format(BasePath, characterInformation.Id)));
            var equipImageTask = characterInformation.Equip == null
                ? Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("hsr_lightcone_template"))
                : Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(
                    string.Format(BasePath, characterInformation.Equip.Id)));
            var rankTasks = characterInformation.Ranks.Select(async x =>
            {
                var image = await Image.LoadAsync(
                    await m_ImageRepository.DownloadFileToStreamAsync(string.Format(BasePath, x.Id)));
                return (Active: x.IsUnlocked!.Value, Image: image);
            }).ToArray();

            var baseSkill = characterInformation.Skills.Where(x => x.PointType == 2 && x.Remake != "Technique")
                .ToArray();
            var skillChains = BuildSkillTree(characterInformation.Skills.Where(x => x.PointType != 2).ToList());

            var baseSkillTasks = baseSkill.Select(async x =>
            {
                var image = await Image.LoadAsync(
                    await m_ImageRepository.DownloadFileToStreamAsync(string.Format(BasePath, x.PointId)));
                return (Data: x, Image: image);
            }).ToArray();
            var skillTasks = skillChains.Select(async chain =>
            {
                var chainImages = await Task.WhenAll(chain.Select(async x =>
                {
                    var image = await Image.LoadAsync(
                        await m_ImageRepository.DownloadFileToStreamAsync(x.PointType == 1
                            ? string.Format(BasePath,
                                HsrImageUpdaterService.StatBonusRegex().Replace(x.SkillStages[0].Name, ""))
                            : string.Format(BasePath, x.PointId)));
                    return (Data: x, Image: image);
                }));
                return chainImages;
            }).ToArray();

            var relicImageTasks = Enumerable.Range(0, 4).Select(async i =>
            {
                var relic = characterInformation.Relics.FirstOrDefault(x => x.Pos == i + 1);
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
            var ornamentImageTasks = Enumerable.Range(0, 2).Select(async i =>
            {
                var ornament = characterInformation.Ornaments.FirstOrDefault(x => x.Pos == i + 5);
                if (ornament != null)
                {
                    var ornamentImage = await CreateRelicSlotImageAsync(ornament);
                    return ornamentImage;
                }
                else
                {
                    var templateOrnamentImage = await CreateTemplateRelicSlotImageAsync(i + 5);
                    return templateOrnamentImage;
                }
            }).ToArray();

            Dictionary<string, int> activeRelicSet = new();
            foreach (var relic in characterInformation.Relics)
            {
                var setName = m_ImageUpdater.GetRelicSetName(relic.Id!.Value);
                if (!activeRelicSet.TryAdd(setName, 1))
                    activeRelicSet[setName]++;
            }

            activeRelicSet = activeRelicSet
                .Where(x => x.Value >= 2)
                .ToDictionary(x => x.Key, x => x.Value);

            Dictionary<string, int> activeOrnamentSet = new();
            foreach (var ornament in characterInformation.Ornaments)
            {
                var setName = m_ImageUpdater.GetRelicSetName(ornament.Id!.Value);
                if (!activeOrnamentSet.TryAdd(setName, 1))
                    activeOrnamentSet[setName]++;
            }

            activeOrnamentSet = activeOrnamentSet
                .Where(x => x.Value >= 2)
                .ToDictionary(x => x.Key, x => x.Value);

            var servantTask = characterInformation.ServantDetail.ServantSkills.Select(async x =>
            {
                var image = await Image.LoadAsync(
                    await m_ImageRepository.DownloadFileToStreamAsync(string.Format(BasePath, x.PointId)));
                return (Data: x, Image: image);
            }).ToArray();

            var accentColor = GetAccentColor(characterInformation.Element);

            await Task.WhenAll(backgroundTask, characterPortraitTask, equipImageTask, Task.WhenAll(rankTasks),
                Task.WhenAll(baseSkillTasks), Task.WhenAll(skillTasks), Task.WhenAll(relicImageTasks),
                Task.WhenAll(ornamentImageTasks), Task.WhenAll(servantTask));

            var backgroundImage = backgroundTask.Result;
            var characterPortrait = characterPortraitTask.Result;
            var equipImage = equipImageTask.Result;
            var ranks = rankTasks.Select(x => x.Result).Reverse().ToArray();
            var baseSkillImages = baseSkillTasks.Select(x => x.Result).ToArray();
            var skillImages = skillTasks.Select(x => x.Result).ToArray();
            var stats = characterInformation.Properties.Where(x =>
                float.Parse(x.Final.TrimEnd('%')) >
                StatMappingUtility.GetDefaultValue(x.PropertyType!.Value, GameName.HonkaiStarRail)).ToList();
            if (stats.Count < 7)
                stats = stats.Concat(characterInformation.Properties).DistinctBy(x => x.PropertyType!.Value).Take(7)
                    .OrderBy(x => x.PropertyType!.Value).ToList();
            var relicImages = relicImageTasks.Select(x => x.Result).ToArray();
            var ornamentImages = ornamentImageTasks.Select(x => x.Result).ToArray();
            var servantImages = servantTask.Select(x => x.Result).ToArray();
            m_Logger.LogInformation("All resources loaded successfully for character card generation");

            disposableResources.AddRange(backgroundImage, characterPortrait, equipImage);
            disposableResources.AddRange(ranks.Select(x => x.Image));
            disposableResources.AddRange(baseSkillImages.Select(x => x.Image));
            disposableResources.AddRange(skillImages.SelectMany(x => x.Select(y => y.Image)));
            disposableResources.AddRange(relicImages);
            disposableResources.AddRange(ornamentImages);
            disposableResources.AddRange(servantImages.Select(x => x.Image));
            // characterPortrait.Mutate(x => x.Resize(1000, 0, KnownResamplers.Bicubic));

            backgroundImage.Mutate(ctx =>
            {
                ctx.DrawImage(characterPortrait,
                    new Point(400 - characterPortrait.Width / 2, 700 - characterPortrait.Height / 2), 1f);
            });

            // draw blur
            var clone = backgroundImage.CloneAs<Rgba32>();
            disposableResources.Add(clone);
            clone.Mutate(ctx =>
            {
                ctx.Crop(new Rectangle(800, 0, 2200, 1200));
                ctx.GaussianBlur(30);
            });

            var overlay = new Image<Rgba32>(2500, 1200);
            disposableResources.Add(overlay);
            overlay.Mutate(ctx =>
            {
                ctx.DrawImage(clone, Point.Empty, 1f);
                ctx.ApplyRoundedCorners(100);
                ctx.Brightness(0.35f);
            });

            backgroundImage.Mutate(ctx =>
            {
                ctx.DrawText(new RichTextOptions(m_TitleFont)
                {
                    Origin = new PointF(73, 53),
                    WrappingLength = 700,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                }, characterInformation.Name, Color.Black);
                ctx.DrawText(new RichTextOptions(m_TitleFont)
                {
                    Origin = new PointF(70, 50),
                    WrappingLength = 700,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                }, characterInformation.Name, Color.White);

                var bounds = TextMeasurer.MeasureBounds(characterInformation.Name, new RichTextOptions(m_TitleFont)
                {
                    Origin = new PointF(70, 50),
                    WrappingLength = 700,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                });

                ctx.DrawText($"Lv. {characterInformation.Level.ToString()!}", m_NormalFont, Color.Black,
                    new PointF(73, bounds.Bottom + 23));
                ctx.DrawText($"Lv. {characterInformation.Level.ToString()!}", m_NormalFont, Color.White,
                    new PointF(70, bounds.Bottom + 20));
                ctx.DrawImage(overlay, new Point(800, 0), 1f);
                ctx.DrawText(gameUid, m_SmallFont, Color.White, new PointF(70, 1150));

                for (int i = 0; i < ranks.Length; i++)
                {
                    var offset = i * 100;
                    var ellipse = new EllipsePolygon(new PointF(900, 1115 - offset), 45);
                    ctx.Fill(new SolidBrush(Color.DarkSlateGray), ellipse);
                    if (!ranks[i].Active) ranks[i].Image.Mutate(x => x.Brightness(0.5f));
                    // ranks[i].Image.Mutate(x => x.Resize(80, 0));
                    ctx.DrawImage(ranks[i].Image, new Point(860, 1075 - offset), 1f);
                    ctx.Draw(accentColor, 5, ellipse.AsClosedPath());
                }

                for (int i = 0; i < baseSkillImages.Length; i++)
                {
                    var offset = i * 100;
                    var ellipse = new EllipsePolygon(new PointF(900, 80 + offset), 45);
                    ctx.Fill(new SolidBrush(Color.DarkSlateGray), ellipse);
                    // baseSkillImages[i].Image.Mutate(x => x.Resize(80, 0));
                    ctx.DrawImage(baseSkillImages[i].Image, new Point(860, 40 + offset), 1f);
                    ctx.Draw(accentColor, 5, ellipse.AsClosedPath());

                    var levelEllipse = new EllipsePolygon(new PointF(865, 115 + offset), 20);
                    ctx.Fill(new SolidBrush(Color.LightSlateGray), levelEllipse);
                    ctx.DrawText(new RichTextOptions(m_SmallFont)
                        {
                            Origin = new PointF(864, 116 + offset),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }, baseSkillImages[i].Data.Level!.ToString()!,
                        baseSkillImages[i].Data.IsRankWork!.Value ? Color.Aqua : Color.White);
                }

                for (int i = 0; i < skillImages.Length; i++)
                {
                    var yOffset = i * 100;
                    for (int j = 0; j < skillImages[i].Length; j++)
                    {
                        var skill = skillImages[i][j];
                        if (!skill.Data.IsActivated!.Value)
                            skill.Image.Mutate(x => x.Brightness(0.5f));

                        if (skill.Data.PointType == 3)
                        {
                            var xOffset = j * 100;
                            var ellipse = new EllipsePolygon(new PointF(1020 + xOffset, 80 + yOffset), 45);
                            // skill.Image.Mutate(x => x.Resize(80, 0));
                            ctx.Fill(new SolidBrush(Color.DarkSlateGray), ellipse);
                            ctx.DrawImage(skill.Image, new Point(980 + xOffset, 40 + yOffset), 1f);
                            ctx.Draw(accentColor, 5, ellipse.AsClosedPath());
                        }
                        else
                        {
                            var xOffset = (j - 1) * 100;
                            var ellipse = new EllipsePolygon(new PointF(1120 + xOffset, 80 + yOffset), 30);
                            // skill.Image.Mutate(x => x.Resize(50, 0));
                            ctx.Fill(new SolidBrush(Color.DarkSlateGray), ellipse);
                            ctx.DrawImage(skill.Image, new Point(1095 + xOffset, 55 + yOffset), 1f);
                            ctx.Draw(accentColor, 5, ellipse.AsClosedPath());
                        }
                    }
                }

                for (int i = 0; i < servantImages.Length; i++)
                {
                    var offset = i * 120;
                    var ellipse = new EllipsePolygon(new PointF(900 + offset, 480), 45);
                    ctx.Fill(new SolidBrush(Color.DarkSlateGray), ellipse);
                    // servantImages[i].Image.Mutate(x => x.Resize(80, 0));
                    ctx.DrawImage(servantImages[i].Image, new Point(860 + offset, 440), 1f);
                    ctx.Draw(accentColor, 5, ellipse.AsClosedPath());

                    var levelEllipse = new EllipsePolygon(new PointF(865 + offset, 515), 20);
                    ctx.Fill(new SolidBrush(Color.LightSlateGray), levelEllipse);
                    ctx.DrawText(new RichTextOptions(m_SmallFont)
                        {
                            Origin = new PointF(864 + offset, 516),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }, servantImages[i].Data.Level!.ToString()!,
                        servantImages[i].Data.IsRankWork ?? false ? Color.Aqua : Color.White);
                }

                if (characterInformation.Equip != null)
                {
                    // equipImage.Mutate(x => x.Resize(300, 0, KnownResamplers.Bicubic));
                    ctx.DrawImage(equipImage, new Point(1000, 730), 1f);
                    ctx.DrawText(new RichTextOptions(m_MediumFont)
                    {
                        Origin = new PointF(1000, 660),
                        WrappingLength = 300,
                        VerticalAlignment = VerticalAlignment.Bottom
                    }, characterInformation.Equip.Name, Color.White);
                    var equipEllipse = new EllipsePolygon(new PointF(1020, 690), 20);
                    ctx.Fill(new SolidBrush(Color.DarkSlateGray), equipEllipse);
                    ctx.DrawText(((char)(0x215F + characterInformation.Equip.Rank!.Value)).ToString(), m_SmallFont,
                        Color.Gold, new PointF(1007, 676));
                    ctx.Draw(Color.Gold, 2f, equipEllipse.AsClosedPath());
                    ctx.DrawText($"Lv. {characterInformation.Equip.Level!.Value}", m_NormalFont, Color.White,
                        new PointF(1080, 670));
                    var stars = ImageExtensions.GenerateFourSidedStarRating(characterInformation.Equip.Rarity!.Value,
                        false);
                    ctx.DrawImage(stars, new Point(990, 730), 1f);
                }
                else
                {
                    var rectangle = new RectangleF(1000, 730, 300, 420);
                    ctx.DrawImage(equipImage, new Point(1000, 805), 1f);
                    ctx.Draw(Color.White, 5f, rectangle);
                    ctx.DrawText(new RichTextOptions(m_MediumFont)
                    {
                        Origin = new PointF(1000, 710),
                        WrappingLength = 300,
                        VerticalAlignment = VerticalAlignment.Bottom
                    }, "No Light Cone", Color.White);
                }

                var statOffset = 1100 / stats.Count;
                for (int i = 0; i < stats.Count; i++)
                {
                    var offset = i * statOffset;
                    var property = stats[i];
                    var statImage = m_StatImages.GetValueOrDefault(property.PropertyType!.Value);
                    if (statImage == null)
                    {
                        m_Logger.LogWarning("Stat image not found for property type {PropertyType}",
                            property.PropertyType);
                        continue;
                    }

                    // statImage.Mutate(x => x.Resize(48, 0, KnownResamplers.Bicubic, true));
                    ctx.DrawImage(statImage, new Point(1400, 75 + offset), 1f);
                    ctx.DrawText(StatMappingUtility.HsrMapping[property.PropertyType!.Value], m_NormalFont, Color.White,
                        new PointF(1460, 80 + offset));
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new PointF(2140, 80 + offset),
                        HorizontalAlignment = HorizontalAlignment.Right
                    }, property.Final, Color.White);
                }

                for (int i = 0; i < relicImages.Length; i++)
                {
                    var offset = i * 170;
                    ctx.DrawImage(relicImages[i], new Point(2200, 50 + offset), 1f);
                }

                var k = 0;
                foreach (var relicSet in activeRelicSet)
                {
                    var offset = k * 30;
                    ctx.DrawText(relicSet.Value.ToString(), m_SmallFont, Color.White,
                        new PointF(2200, 720 + offset));
                    ctx.DrawText(relicSet.Key, m_SmallFont, Color.White, new PointF(2230, 720 + offset));
                    k++;
                }

                for (int i = 0; i < ornamentImages.Length; i++)
                {
                    var offset = i * 170;
                    ctx.DrawImage(ornamentImages[ornamentImages.Length - 1 - i], new Point(2200, 1000 - offset), 1f);
                }

                k = 0;
                foreach (var ornamentSet in activeOrnamentSet)
                {
                    var offset = k * 30;
                    ctx.DrawText(new RichTextOptions(m_SmallFont)
                    {
                        Origin = new PointF(2940, 820 - offset),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Bottom
                    }, ornamentSet.Value.ToString(), Color.White);
                    ctx.DrawText(new RichTextOptions(m_SmallFont)
                    {
                        Origin = new PointF(2910, 820 - offset),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Bottom
                    }, ornamentSet.Key, Color.White);
                    k++;
                }
            });

            MemoryStream memoryStream = new();
            await backgroundImage.SaveAsJpegAsync(memoryStream, m_JpegEncoder);
            memoryStream.Position = 0;
            m_Logger.LogInformation("Character card generated successfully for {CharacterName}",
                characterInformation.Name);
            return memoryStream;
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Failed to load background image for character card");
            throw;
        }
        finally
        {
            disposableResources.ForEach(x => x.Dispose());
        }
    }

    private async Task<Image<Rgba32>> CreateRelicSlotImageAsync(Relic relic)
    {
        var relicImage = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(
            string.Format(BasePath, relic.Id)));
        var relicSlotImage = m_RelicSlotTemplate.Clone();
        // relicImage.Mutate(x =>
        // {
        //     x.Resize(150, 0);
        //     x.ApplyGradientFade(0.5f);
        // });
        relicSlotImage.Mutate(ctx =>
        {
            ctx.DrawImage(relicImage, new Point(10, 0), 1f);
            ctx.DrawImage(ImageExtensions.GenerateFourSidedStarRating(relic.Rarity!.Value), new Point(20, 115), 1f);
            ctx.DrawImage(m_StatImages[relic.MainProperty.PropertyType!.Value], new Point(125, 10), 1f);
            ctx.DrawText(new RichTextOptions(m_NormalFont)
            {
                Origin = new PointF(230, 60),
                HorizontalAlignment = HorizontalAlignment.Right
            }, relic.MainProperty.Value, Color.White);
            ctx.DrawText($"+{relic.Level}", m_SmallFont, Color.White, new PointF(180, 20));

            for (var i = 0; i < relic.Properties.Count; i++)
            {
                var subStat = relic.Properties[i];
                var subStatImage = m_StatImages[subStat.PropertyType!.Value];
                var xOffset = i % 2 * 245;
                var yOffset = i / 2 * 70;
                ctx.DrawImage(subStatImage, new Point(260 + xOffset, 15 + yOffset), 1f);
                ctx.DrawText(subStat.Value, m_NormalFont, Color.White, new PointF(310 + xOffset, 20 + yOffset));
                var rolls = string.Concat(Enumerable.Repeat('.', subStat.Times.GetValueOrDefault(0) + 1));
                ctx.DrawText(rolls, m_NormalFont, Color.White, new PointF(435 + xOffset, 10 + yOffset));
            }

            relicImage.Dispose();
        });
        return relicSlotImage;
    }

    private async Task<Image> CreateTemplateRelicSlotImageAsync(int slot)
    {
        var path = $"hsr_relic_template_{slot}";
        m_Logger.LogDebug("Loading template relic image from {Path}", path);

        var relicImage = await Image.LoadAsync<Rgba32>(
            await m_ImageRepository.DownloadFileToStreamAsync(path));
        // relicImage.Mutate(x => x.Resize(new Size(0, 130), KnownResamplers.Bicubic, true));
        var template = m_RelicSlotTemplate.Clone();
        template.Mutate(ctx =>
        {
            ctx.DrawImage(relicImage, new Point(25, 10), 1f);
            ctx.DrawText(new RichTextOptions(m_NormalFont)
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
        var result = new List<List<Skill>>();
        var skillLookup = skills.ToDictionary(s => s.PointId, s => s);
        var processed = new HashSet<string>();

        // Find all skills that have point_type == 3 and can be roots
        var type3Skills = skills.Where(s => s.PointType == 3).ToList();
        var potentialRoots = type3Skills.Where(skill =>
                string.IsNullOrEmpty(skill.PrePoint) || skill.PrePoint == "0" ||
                !skillLookup.ContainsKey(skill.PrePoint))
            .ToList();

        // Check which type 3 skills can actually be roots (their pre_point either doesn't exist in our filtered list or is "0")

        // Build chains from each root
        foreach (var rootSkill in potentialRoots)
        {
            if (processed.Contains(rootSkill.PointId))
                continue;

            var chain = BuildChainFromRoot(rootSkill, processed, skills);
            if (chain.Count > 0) result.Add(chain);
        }

        // Collect any remaining unprocessed skills
        var unprocessedSkills = skills
            .Where(skill => !processed.Contains(skill.PointId))
            .OrderBy(skill => skill.Anchor) // Sort by anchor as required
            .ToList();

        // If there are unprocessed skills, add them as the first chain
        if (unprocessedSkills.Count > 0) result.Insert(0, unprocessedSkills);

        return result;
    }

    private static List<Skill> BuildChainFromRoot(Skill rootSkill, HashSet<string> processed, List<Skill> allSkills)
    {
        var chain = new List<Skill>();
        var queue = new Queue<Skill>();
        queue.Enqueue(rootSkill);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (processed.Contains(current.PointId))
                continue;

            chain.Add(current);
            processed.Add(current.PointId);

            // Find all skills that have this skill as their pre_point
            var childSkills = allSkills
                .Where(s => s.PrePoint == current.PointId && !processed.Contains(s.PointId))
                .ToList();

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
