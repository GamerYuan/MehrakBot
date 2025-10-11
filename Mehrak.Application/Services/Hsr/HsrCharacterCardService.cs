#region

using Mehrak.Domain.Common;
using Mehrak.Domain.Utilities;
using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Models;
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

namespace Mehrak.Application.Services.Hsr;

public class HsrCharacterCardService : ICharacterCardService<HsrCharacterInformation>
{
    private readonly ImageRepository m_ImageRepository;
    private readonly IRelicRepository m_RelicRepository;
    private readonly ILogger<HsrCharacterCardService> m_Logger;

    private Dictionary<int, Image> m_StatImages = null!;

    private const string BasePath = FileNameFormat.HsrFileName;
    private const string StatsPath = FileNameFormat.HsrStatsName;

    private readonly Font m_SmallFont;
    private readonly Font m_NormalFont;
    private readonly Font m_MediumFont;
    private readonly Font m_TitleFont;

    private readonly JpegEncoder m_JpegEncoder;
    private readonly Image<Rgba32> m_RelicSlotTemplate;

    public HsrCharacterCardService(ImageRepository imageRepository,
        IRelicRepository relicRepository,
        ILogger<HsrCharacterCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_RelicRepository = relicRepository;
        m_Logger = logger;

        FontFamily fontFamily = new FontCollection().Add("Assets/Fonts/hsr.ttf");

        m_TitleFont = fontFamily.CreateFont(64);
        m_NormalFont = fontFamily.CreateFont(40);
        m_MediumFont = fontFamily.CreateFont(36);
        m_SmallFont = fontFamily.CreateFont(28);

        m_JpegEncoder = new JpegEncoder
        {
            Quality = 90,
            Interleaved = false
        };

        m_RelicSlotTemplate = new Image<Rgba32>(750, 150);
        m_RelicSlotTemplate.Mutate(x =>
        {
            x.Fill(new Rgba32(255, 255, 255, 0.1f));
            x.ApplyRoundedCorners(30);
        });
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        int[] statIds =
        [
            1, 2, 3, 4, 5, 6, 7, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30,
                    31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60
        ];

        m_StatImages = await statIds.ToAsyncEnumerable().Where(x => x != 8).SelectAwait(async x =>
        {
            string path = string.Format(StatsPath, x);
            m_Logger.LogTrace("Downloading stat icon {StatId}: {Path}", x, path);
            Image image = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(path));
            return new KeyValuePair<int, Image>(x, image);
        }).ToDictionaryAsync(x => x.Key, x => x.Value, cancellationToken: cancellationToken);

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
            Task<Image> backgroundTask = Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("hsr_bg"));

            Task<Image<Rgba32>> characterPortraitTask = Image.LoadAsync<Rgba32>(await m_ImageRepository.DownloadFileToStreamAsync(
                string.Format(BasePath, characterInformation.Id)));
            Task<Image> equipImageTask = characterInformation.Equip == null
                ? Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("hsr_lightcone_template"))
                : Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(
                    string.Format(BasePath, characterInformation.Equip.Id)));
            Task<(bool Active, Image Image)>[] rankTasks = characterInformation.Ranks!.Select(async x =>
            {
                Image image = await Image.LoadAsync(
                    await m_ImageRepository.DownloadFileToStreamAsync(string.Format(BasePath, x.Id)));
                return (Active: x.IsUnlocked!.Value, Image: image);
            }).ToArray();

            Skill[] baseSkill = characterInformation.Skills!.Where(x => x.PointType == 2 && x.Remake != "Technique")
                .ToArray();
            List<List<Skill>> skillChains = BuildSkillTree([.. characterInformation.Skills!.Where(x => x.PointType != 2)]);

            Task<(Skill Data, Image Image)>[] baseSkillTasks = baseSkill.Select(async x =>
            {
                Image image = await Image.LoadAsync(
                    await m_ImageRepository.DownloadFileToStreamAsync(string.Format(BasePath, x.PointId)));
                return (Data: x, Image: image);
            }).ToArray();
            Task<(Skill Data, Image Image)[]>[] skillTasks = skillChains.Select(async chain =>
            {
                (Skill Data, Image Image)[] chainImages = await Task.WhenAll(chain.Select(async x =>
                {
                    Image image = await Image.LoadAsync(
                        await m_ImageRepository.DownloadFileToStreamAsync(x.PointType == 1
                            ? string.Format(BasePath,
                                HsrImageUpdaterService.StatBonusRegex().Replace(x.SkillStages![0].Name!, ""))
                            : string.Format(BasePath, x.PointId)));
                    return (Data: x, Image: image);
                }));
                return chainImages;
            }).ToArray();

            Task<Image>[] relicImageTasks = Enumerable.Range(0, 4).Select(async i =>
            {
                Relic? relic = characterInformation.Relics!.FirstOrDefault(x => x.Pos == i + 1);
                if (relic != null)
                {
                    Image<Rgba32> relicImage = await CreateRelicSlotImageAsync(relic);
                    return relicImage;
                }
                else
                {
                    Image templateRelicImage = await CreateTemplateRelicSlotImageAsync(i + 1);
                    return templateRelicImage;
                }
            }).ToArray();
            Task<Image>[] ornamentImageTasks = Enumerable.Range(0, 2).Select(async i =>
            {
                Relic? ornament = characterInformation.Ornaments!.FirstOrDefault(x => x.Pos == i + 5);
                if (ornament != null)
                {
                    Image<Rgba32> ornamentImage = await CreateRelicSlotImageAsync(ornament);
                    return ornamentImage;
                }
                else
                {
                    Image templateOrnamentImage = await CreateTemplateRelicSlotImageAsync(i + 5);
                    return templateOrnamentImage;
                }
            }).ToArray();

            Dictionary<string, int> activeRelicSet = [];
            foreach (int setId in characterInformation.Relics!.Select(x => x.GetSetId()))
            {
                string setName = await m_RelicRepository.GetSetName(setId);
                if (!activeRelicSet.TryAdd(setName, 1))
                    activeRelicSet[setName]++;
            }

            activeRelicSet = activeRelicSet
                .Where(x => x.Value >= 2)
                .ToDictionary(x => x.Key, x => x.Value);

            Dictionary<string, int> activeOrnamentSet = [];
            foreach (int setId in characterInformation.Ornaments!.Select(x => x.GetSetId()))
            {
                string setName = await m_RelicRepository.GetSetName(setId);
                if (!activeOrnamentSet.TryAdd(setName, 1))
                    activeOrnamentSet[setName]++;
            }

            activeOrnamentSet = activeOrnamentSet
                .Where(x => x.Value >= 2)
                .ToDictionary(x => x.Key, x => x.Value);

            Task<(Skill Data, Image Image)>[] servantTask = characterInformation.ServantDetail!.ServantSkills!.Select(async x =>
            {
                Image image = await Image.LoadAsync(
                    await m_ImageRepository.DownloadFileToStreamAsync(string.Format(BasePath, x.PointId)));
                return (Data: x, Image: image);
            }).ToArray();

            Color accentColor = GetAccentColor(characterInformation.Element!);

            await Task.WhenAll(backgroundTask, characterPortraitTask, equipImageTask, Task.WhenAll(rankTasks),
                Task.WhenAll(baseSkillTasks), Task.WhenAll(skillTasks), Task.WhenAll(relicImageTasks),
                Task.WhenAll(ornamentImageTasks), Task.WhenAll(servantTask));

            Image backgroundImage = backgroundTask.Result;
            Image<Rgba32> characterPortrait = characterPortraitTask.Result;
            Image equipImage = equipImageTask.Result;
            (bool Active, Image Image)[] ranks = rankTasks.Select(x => x.Result).Reverse().ToArray();
            (Skill Data, Image Image)[] baseSkillImages = baseSkillTasks.Select(x => x.Result).ToArray();
            (Skill Data, Image Image)[][] skillImages = skillTasks.Select(x => x.Result).ToArray();
            List<Property> stats = characterInformation.Properties!.Where(x =>
                float.Parse(x.Final!.TrimEnd('%')) >
                StatMappingUtility.GetDefaultValue(x.PropertyType!.Value, Game.HonkaiStarRail)).ToList();
            if (stats.Count < 7)
                stats = [.. stats.Concat(characterInformation.Properties!)
                    .DistinctBy(x => x.PropertyType!.Value).Take(7).OrderBy(x => x.PropertyType!.Value)];
            Image[] relicImages = relicImageTasks.Select(x => x.Result).ToArray();
            Image[] ornamentImages = ornamentImageTasks.Select(x => x.Result).ToArray();
            (Skill Data, Image Image)[] servantImages = servantTask.Select(x => x.Result).ToArray();
            m_Logger.LogInformation("All resources loaded successfully for character card generation");

            disposableResources.AddRange(backgroundImage, characterPortrait, equipImage);
            disposableResources.AddRange(ranks.Select(x => x.Image));
            disposableResources.AddRange(baseSkillImages.Select(x => x.Image));
            disposableResources.AddRange(skillImages.SelectMany(x => x.Select(y => y.Image)));
            disposableResources.AddRange(relicImages);
            disposableResources.AddRange(ornamentImages);
            disposableResources.AddRange(servantImages.Select(x => x.Image));

            backgroundImage.Mutate(ctx =>
            {
                ctx.DrawImage(characterPortrait,
                    new Point(400 - characterPortrait.Width / 2, 700 - characterPortrait.Height / 2), 1f);
            });

            // draw blur
            Image<Rgba32> clone = backgroundImage.CloneAs<Rgba32>();
            disposableResources.Add(clone);
            clone.Mutate(ctx =>
            {
                ctx.Crop(new Rectangle(800, 0, 2200, 1200));
                ctx.GaussianBlur(30);
            });

            Image<Rgba32> overlay = new(2500, 1200);
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
                }, characterInformation.Name!, Color.Black);
                ctx.DrawText(new RichTextOptions(m_TitleFont)
                {
                    Origin = new PointF(70, 50),
                    WrappingLength = 700,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                }, characterInformation.Name!, Color.White);

                FontRectangle bounds = TextMeasurer.MeasureBounds(characterInformation.Name!, new RichTextOptions(m_TitleFont)
                {
                    Origin = new PointF(70, 50),
                    WrappingLength = 700,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                });

                ctx.DrawText($"Lv. {characterInformation.Level}", m_NormalFont, Color.Black,
                    new PointF(73, bounds.Bottom + 23));
                ctx.DrawText($"Lv. {characterInformation.Level}", m_NormalFont, Color.White,
                    new PointF(70, bounds.Bottom + 20));
                ctx.DrawImage(overlay, new Point(800, 0), 1f);
                ctx.DrawText(gameUid, m_SmallFont, Color.White, new PointF(70, 1150));

                for (int i = 0; i < ranks.Length; i++)
                {
                    int offset = i * 100;
                    EllipsePolygon ellipse = new(new PointF(900, 1115 - offset), 45);
                    ctx.Fill(new SolidBrush(Color.DarkSlateGray), ellipse);
                    if (!ranks[i].Active) ranks[i].Image.Mutate(x => x.Brightness(0.5f));
                    ctx.DrawImage(ranks[i].Image, new Point(860, 1075 - offset), 1f);
                    ctx.Draw(accentColor, 5, ellipse.AsClosedPath());
                }

                for (int i = 0; i < baseSkillImages.Length; i++)
                {
                    int offset = i * 100;
                    EllipsePolygon ellipse = new(new PointF(900, 80 + offset), 45);
                    ctx.Fill(new SolidBrush(Color.DarkSlateGray), ellipse);
                    ctx.DrawImage(baseSkillImages[i].Image, new Point(860, 40 + offset), 1f);
                    ctx.Draw(accentColor, 5, ellipse.AsClosedPath());

                    EllipsePolygon levelEllipse = new(new PointF(865, 115 + offset), 20);
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
                    int yOffset = i * 100;
                    for (int j = 0; j < skillImages[i].Length; j++)
                    {
                        (Skill Data, Image Image) skill = skillImages[i][j];
                        if (!skill.Data.IsActivated!.Value)
                            skill.Image.Mutate(x => x.Brightness(0.5f));

                        if (skill.Data.PointType == 3)
                        {
                            int xOffset = j * 100;
                            EllipsePolygon ellipse = new(new PointF(1020 + xOffset, 80 + yOffset), 45);
                            ctx.Fill(new SolidBrush(Color.DarkSlateGray), ellipse);
                            ctx.DrawImage(skill.Image, new Point(980 + xOffset, 40 + yOffset), 1f);
                            ctx.Draw(accentColor, 5, ellipse.AsClosedPath());
                        }
                        else
                        {
                            int xOffset = (j - 1) * 100;
                            EllipsePolygon ellipse = new(new PointF(1120 + xOffset, 80 + yOffset), 30);
                            ctx.Fill(new SolidBrush(Color.DarkSlateGray), ellipse);
                            ctx.DrawImage(skill.Image, new Point(1095 + xOffset, 55 + yOffset), 1f);
                            ctx.Draw(accentColor, 5, ellipse.AsClosedPath());
                        }
                    }
                }

                for (int i = 0; i < servantImages.Length; i++)
                {
                    int offset = i * 120;
                    EllipsePolygon ellipse = new(new PointF(900 + offset, 480), 45);
                    ctx.Fill(new SolidBrush(Color.DarkSlateGray), ellipse);
                    ctx.DrawImage(servantImages[i].Image, new Point(860 + offset, 440), 1f);
                    ctx.Draw(accentColor, 5, ellipse.AsClosedPath());

                    EllipsePolygon levelEllipse = new(new PointF(865 + offset, 515), 20);
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
                    ctx.DrawImage(equipImage, new Point(1000, 730), 1f);
                    ctx.DrawText(new RichTextOptions(m_MediumFont)
                    {
                        Origin = new PointF(1000, 660),
                        WrappingLength = 300,
                        VerticalAlignment = VerticalAlignment.Bottom
                    }, characterInformation.Equip.Name!, Color.White);
                    EllipsePolygon equipEllipse = new(new PointF(1020, 690), 20);
                    ctx.Fill(new SolidBrush(Color.DarkSlateGray), equipEllipse);
                    ctx.DrawText(((char)(0x215F + characterInformation.Equip.Rank!.Value)).ToString(), m_SmallFont,
                        Color.Gold, new PointF(1007, 676));
                    ctx.Draw(Color.Gold, 2f, equipEllipse.AsClosedPath());
                    ctx.DrawText($"Lv. {characterInformation.Equip.Level!.Value}", m_NormalFont, Color.White,
                        new PointF(1080, 670));
                    Image<Rgba32> stars = ImageUtility.GenerateFourSidedStarRating(characterInformation.Equip.Rarity!.Value,
                        false);
                    ctx.DrawImage(stars, new Point(990, 730), 1f);
                }
                else
                {
                    RectangleF rectangle = new(1000, 730, 300, 420);
                    ctx.DrawImage(equipImage, new Point(1000, 805), 1f);
                    ctx.Draw(Color.White, 5f, rectangle);
                    ctx.DrawText(new RichTextOptions(m_MediumFont)
                    {
                        Origin = new PointF(1000, 710),
                        WrappingLength = 300,
                        VerticalAlignment = VerticalAlignment.Bottom
                    }, "No Light Cone", Color.White);
                }

                int statOffset = 1100 / stats.Count;
                for (int i = 0; i < stats.Count; i++)
                {
                    int offset = i * statOffset;
                    Property property = stats[i];
                    Image? statImage = m_StatImages.GetValueOrDefault(property.PropertyType!.Value);
                    if (statImage == null)
                    {
                        m_Logger.LogWarning("Stat image not found for property type {PropertyType}",
                            property.PropertyType);
                        continue;
                    }

                    ctx.DrawImage(statImage, new Point(1400, 75 + offset), 1f);
                    ctx.DrawText(StatMappingUtility.HsrMapping[property.PropertyType!.Value], m_NormalFont, Color.White,
                        new PointF(1460, 80 + offset));
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new PointF(2140, 80 + offset),
                        HorizontalAlignment = HorizontalAlignment.Right
                    }, property.Final!, Color.White);
                }

                for (int i = 0; i < relicImages.Length; i++)
                {
                    int offset = i * 170;
                    ctx.DrawImage(relicImages[i], new Point(2200, 50 + offset), 1f);
                }

                int k = 0;
                foreach (KeyValuePair<string, int> relicSet in activeRelicSet)
                {
                    int offset = k * 30;
                    ctx.DrawText(relicSet.Value.ToString(), m_SmallFont, Color.White,
                        new PointF(2200, 720 + offset));
                    ctx.DrawText(relicSet.Key, m_SmallFont, Color.White, new PointF(2230, 720 + offset));
                    k++;
                }

                for (int i = 0; i < ornamentImages.Length; i++)
                {
                    int offset = i * 170;
                    ctx.DrawImage(ornamentImages[ornamentImages.Length - 1 - i], new Point(2200, 1000 - offset), 1f);
                }

                k = 0;
                foreach (KeyValuePair<string, int> ornamentSet in activeOrnamentSet)
                {
                    int offset = k * 30;
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
            m_Logger.LogError(ex, "Failed to generate character card for Character {CharacterInfo}",
                characterInformation.ToString());
            throw new CommandException("An error occurred while generating the character card", ex);
        }
        finally
        {
            disposableResources.ForEach(x => x.Dispose());
        }
    }

    private async Task<Image<Rgba32>> CreateRelicSlotImageAsync(Relic relic)
    {
        Image relicImage = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(
            string.Format(BasePath, relic.Id)));
        Image<Rgba32> relicSlotImage = m_RelicSlotTemplate.Clone();
        relicSlotImage.Mutate(ctx =>
        {
            ctx.DrawImage(relicImage, new Point(10, 0), 1f);
            ctx.DrawImage(ImageUtility.GenerateFourSidedStarRating(relic.Rarity!.Value), new Point(20, 115), 1f);
            ctx.DrawImage(m_StatImages[relic.MainProperty!.PropertyType!.Value], new Point(125, 10), 1f);
            ctx.DrawText(new RichTextOptions(m_NormalFont)
            {
                Origin = new PointF(230, 60),
                HorizontalAlignment = HorizontalAlignment.Right
            }, relic.MainProperty!.Value!, Color.White);
            ctx.DrawText($"+{relic.Level}", m_SmallFont, Color.White, new PointF(180, 20));

            for (int i = 0; i < relic.Properties!.Count; i++)
            {
                Property subStat = relic.Properties[i];
                Image subStatImage = m_StatImages[subStat.PropertyType!.Value];
                int xOffset = i % 2 * 245;
                int yOffset = i / 2 * 70;
                Color color = Color.White;
                if (subStat.PropertyType is 27 or 29 or 31)
                {
                    Image<Rgba32> dim = subStatImage.CloneAs<Rgba32>();
                    dim.Mutate(x => x.Brightness(0.5f));
                    ctx.DrawImage(dim, new Point(260 + xOffset, 15 + yOffset), 1f);
                    color = Color.FromRgb(128, 128, 128);
                }
                else
                {
                    ctx.DrawImage(subStatImage, new Point(260 + xOffset, 15 + yOffset), 1f);
                }

                ctx.DrawText(subStat.Value!, m_NormalFont, color, new PointF(310 + xOffset, 20 + yOffset));
                string rolls = string.Concat(Enumerable.Repeat('.', subStat.Times.GetValueOrDefault(0)));
                ctx.DrawText(rolls, m_NormalFont, color, new PointF(435 + xOffset, 10 + yOffset));
            }

            relicImage.Dispose();
        });
        return relicSlotImage;
    }

    private async Task<Image> CreateTemplateRelicSlotImageAsync(int slot)
    {
        string path = $"hsr_relic_template_{slot}";
        m_Logger.LogDebug("Loading template relic image from {Path}", path);

        Image<Rgba32> relicImage = await Image.LoadAsync<Rgba32>(
            await m_ImageRepository.DownloadFileToStreamAsync(path));
        Image<Rgba32> template = m_RelicSlotTemplate.Clone();
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
        List<List<Skill>> result = [];
        Dictionary<string, Skill> skillLookup = skills.ToDictionary(s => s.PointId!, s => s);
        HashSet<string> processed = [];

        // Find all skills that have point_type == 3 and can be roots
        List<Skill> type3Skills = skills.Where(s => s.PointType == 3).ToList();
        List<Skill> potentialRoots = type3Skills.Where(skill =>
                string.IsNullOrEmpty(skill.PrePoint) || skill.PrePoint == "0" ||
                !skillLookup.ContainsKey(skill.PrePoint))
            .ToList();

        // Check which type 3 skills can actually be roots (their pre_point
        // either doesn't exist in our filtered list or is "0")

        // Build chains from each root
        foreach (Skill? rootSkill in potentialRoots)
        {
            if (processed.Contains(rootSkill.PointId!))
                continue;

            List<Skill> chain = BuildChainFromRoot(rootSkill, processed, skills);
            if (chain.Count > 0) result.Add(chain);
        }

        // Collect any remaining unprocessed skills
        List<Skill> unprocessedSkills = skills
            .Where(skill => !processed.Contains(skill.PointId!))
            .OrderBy(skill => skill.Anchor) // Sort by anchor as required
            .ToList();

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
            Skill current = queue.Dequeue();

            if (processed.Contains(current.PointId!))
                continue;

            chain.Add(current);
            processed.Add(current.PointId!);

            // Find all skills that have this skill as their pre_point
            List<Skill> childSkills = allSkills
                .Where(s => s.PrePoint == current.PointId && !processed.Contains(s.PointId!))
                .ToList();

            foreach (Skill? childSkill in childSkills) queue.Enqueue(childSkill);
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
