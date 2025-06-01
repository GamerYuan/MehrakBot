#region

using MehrakCore.ApiResponseTypes.Hsr;
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
    private readonly ILogger<HsrCharacterCardService> m_Logger;

    private readonly Dictionary<int, Image> m_StatImages;

    private const string BasePath = "hsr_{0}";
    private const string StatsPath = "hsr_stats_{0}";

    private readonly Font m_SmallFont;
    private readonly Font m_MediumFont;
    private readonly Font m_NormalFont;
    private readonly Font m_TitleFont;

    private readonly JpegEncoder m_JpegEncoder;
    private readonly Image<Rgba32> m_RelicSlotTemplate;


    public HsrCharacterCardService(ImageRepository imageRepository, ILogger<HsrCharacterCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_Logger = logger;

        var fontFamily = new FontCollection().Add("Fonts/hsr.ttf");

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
        [
            1, 2, 3, 4, 5, 6, 7, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30,
            31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60
        ];

        m_StatImages = statIds.ToAsyncEnumerable().Where(x => x != 8).SelectAwait(async x =>
        {
            var path = string.Format(StatsPath, x);
            m_Logger.LogTrace("Downloading stat icon {StatId}: {Path}", x, path);
            var image = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(path));
            image.Mutate(ctx => ctx.Resize(new Size(48, 0), KnownResamplers.Bicubic, true));
            return new KeyValuePair<int, Image>(x, image);
        }).ToBlockingEnumerable().ToDictionary(x => x.Key, x => x.Value);

        m_Logger.LogInformation("Resources initialized successfully with {Count} icons.", m_StatImages.Count);

        m_Logger.LogInformation("HsrCharacterCardService initialized");
    }

    public async Task<Stream> GenerateCharacterCardAsync(HsrCharacterInformation characterInformation, string gameUid)
    {
        m_Logger.LogInformation("Generating character card for {CharacterName}", characterInformation.Name);

        List<IDisposable> disposableResources = [];

        try
        {
            var backgroundTask = Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("hsr_bg"));

            var characterPortraitTask = Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(
                string.Format(BasePath, characterInformation.Id)));
            var equipImageTask = Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(
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
                        await m_ImageRepository.DownloadFileToStreamAsync(string.Format(BasePath, x.PointId)));
                    return (Data: x, Image: image);
                }));
                return chainImages;
            }).ToArray();

            // var relicImageTasks = Enumerable.Range(0, 5).Select(async i =>
            // {
            //     var relic = characterInformation.Relics.FirstOrDefault(x => x.Pos == i + 1);
            //     if (relic != null)
            //     {
            //         var relicImage = await CreateRelicSlotImageAsync(relic);
            //         return relicImage;
            //     }
            //     else
            //     {
            //         var templateRelicImage = await CreateTemplateRelicSlotImageAsync(i + 1);
            //         return templateRelicImage;
            //     }
            // }).ToArray();
            // var ornamentImageTasks = Enumerable.Range(0, 2).Select(async i =>
            // {
            //     var ornament = characterInformation.Ornaments.FirstOrDefault(x => x.Pos == i + 1);
            //     if (ornament != null)
            //     {
            //         var ornamentImage = await CreateRelicSlotImageAsync(ornament);
            //         return ornamentImage;
            //     }
            //     else
            //     {
            //         var templateOrnamentImage = await CreateTemplateRelicSlotImageAsync(i + 1);
            //         return templateOrnamentImage;
            //     }
            // }).ToArray();

            await Task.WhenAll(backgroundTask, characterPortraitTask, equipImageTask, Task.WhenAll(rankTasks),
                Task.WhenAll(baseSkillTasks),
                Task.WhenAll(skillTasks) /*, Task.WhenAll(relicImageTasks), Task.WhenAll(ornamentImageTasks)*/);

            var backgroundImage = backgroundTask.Result;
            var characterPortrait = characterPortraitTask.Result;
            var equipImage = equipImageTask.Result;
            var ranks = rankTasks.Select(x => x.Result).Reverse().ToArray();
            var baseSkillImages = baseSkillTasks.Select(x => x.Result).ToArray();
            var skillImages = skillTasks.Select(x => x.Result).ToArray();
            // var relicImages = await Task.WhenAll(relicImageTasks);
            // var ornamentImages = await Task.WhenAll(ornamentImageTasks);
            m_Logger.LogInformation("All resources loaded successfully for character card generation");

            disposableResources.AddRange(backgroundImage, characterPortrait, equipImage);
            disposableResources.AddRange(ranks.Select(x => x.Image));
            disposableResources.AddRange(baseSkillImages.Select(x => x.Image));
            disposableResources.AddRange(skillImages.SelectMany(x => x.Select(y => y.Image)));
            // disposableResources.AddRange(relicImages);
            // disposableResources.AddRange(ornamentImages);
            characterPortrait.Mutate(x => x.Resize(1000, 0, KnownResamplers.Bicubic));

            backgroundImage.Mutate(ctx =>
            {
                ctx.DrawImage(characterPortrait,
                    new Point(400 - characterPortrait.Width / 2, 800 - characterPortrait.Height / 2), 1f);
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
                ctx.DrawText(characterInformation.Name, m_TitleFont, Color.Black, new PointF(73, 53));
                ctx.DrawText(characterInformation.Name, m_TitleFont, Color.White, new PointF(70, 50));
                ctx.DrawText($"Lv. {characterInformation.Level.ToString()!}", m_NormalFont, Color.Black,
                    new PointF(73, 113));
                ctx.DrawText($"Lv. {characterInformation.Level.ToString()!}", m_NormalFont, Color.White,
                    new PointF(70, 110));
                ctx.DrawImage(overlay, new Point(800, 0), 1f);

                for (int i = 0; i < ranks.Length; i++)
                {
                    var offset = i * 100;
                    var ellipse = new EllipsePolygon(new PointF(900, 1115 - offset), 45);
                    ctx.Fill(new SolidBrush(Color.DarkSlateGray), ellipse);
                    if (!ranks[i].Active) ranks[i].Image.Mutate(x => x.Brightness(0.5f));
                    ranks[i].Image.Mutate(x => x.Resize(80, 0));
                    ctx.DrawImage(ranks[i].Image, new Point(860, 1075 - offset), 1f);
                    ctx.Draw(Color.DarkBlue, 5, ellipse.AsClosedPath());
                }

                for (int i = 0; i < baseSkillImages.Length; i++)
                {
                    var offset = i * 110;
                    var ellipse = new EllipsePolygon(new PointF(895, 90 + offset), 50);
                    ctx.Fill(new SolidBrush(Color.DarkSlateGray), ellipse);
                    baseSkillImages[i].Image.Mutate(x => x.Resize(90, 0));
                    ctx.DrawImage(baseSkillImages[i].Image, new Point(850, 45 + offset), 1f);
                    ctx.Draw(Color.DarkBlue, 5, ellipse.AsClosedPath());

                    var levelEllipse = new EllipsePolygon(new PointF(860, 125 + offset), 20);
                    ctx.Fill(new SolidBrush(Color.LightSlateGray), levelEllipse);
                    ctx.DrawText(new RichTextOptions(m_SmallFont)
                        {
                            Origin = new PointF(859, 126 + offset),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }, baseSkillImages[i].Data.Level!.ToString()!,
                        baseSkillImages[i].Data.IsRankWork!.Value ? Color.Aqua : Color.White);
                }

                for (int i = 0; i < skillImages.Length; i++)
                {
                    var yOffset = i * 110;
                    for (int j = 0; j < skillImages[i].Length; j++)
                    {
                        var skill = skillImages[i][j];
                        if (!skill.Data.IsActivated!.Value)
                            skill.Image.Mutate(x => x.Brightness(0.5f));

                        if (skill.Data.PointType == 3)
                        {
                            var xOffset = j * 100;
                            var ellipse = new EllipsePolygon(new PointF(1005 + xOffset, 90 + yOffset), 45);
                            skill.Image.Mutate(x => x.Resize(80, 0));
                            ctx.Fill(new SolidBrush(Color.DarkSlateGray), ellipse);
                            ctx.DrawImage(skill.Image, new Point(965 + xOffset, 50 + yOffset), 1f);
                            ctx.Draw(Color.DarkBlue, 5, ellipse.AsClosedPath());
                        }
                        else
                        {
                            var xOffset = (j - 1) * 100;
                            var ellipse = new EllipsePolygon(new PointF(1105 + xOffset, 95 + yOffset), 30);
                            skill.Image.Mutate(x => x.Resize(50, 0));
                            ctx.Fill(new SolidBrush(Color.DarkSlateGray), ellipse);
                            ctx.DrawImage(skill.Image, new Point(1080 + xOffset, 70 + yOffset), 1f);
                            ctx.Draw(Color.DarkBlue, 5, ellipse.AsClosedPath());
                        }
                    }
                }

                if (characterInformation.Equip != null)
                {
                    equipImage.Mutate(x => x.Resize(300, 0, KnownResamplers.Bicubic));
                    ctx.DrawImage(equipImage, new Point(1000, 700), 1f);
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new PointF(1000, 630),
                        WrappingLength = 300,
                        VerticalAlignment = VerticalAlignment.Bottom
                    }, characterInformation.Equip.Name, Color.White);
                    var equipEllipse = new EllipsePolygon(new PointF(1020, 660), 20);
                    ctx.Fill(new SolidBrush(Color.DarkSlateGray), equipEllipse);
                    ctx.DrawText(((char)(0x215F + characterInformation.Equip.Rank!.Value)).ToString(), m_SmallFont,
                        Color.Gold, new PointF(1007, 646));
                    ctx.Draw(Color.Gold, 2f, equipEllipse.AsClosedPath());
                    ctx.DrawText($"Lv. {characterInformation.Equip.Level!.Value}", m_NormalFont, Color.White,
                        new PointF(1080, 640));
                    var stars = ImageExtensions.GenerateFourSidedStarRating(characterInformation.Equip.Rarity!.Value,
                        false);
                    ctx.DrawImage(stars, new Point(990, 700), 1f);
                }
                else
                {
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
        relicSlotImage.Mutate(ctx => ctx.DrawImage(relicImage, new Point(0, 0), 1f));
        return relicSlotImage;
    }

    private async Task<Image> CreateTemplateRelicSlotImageAsync(int slot)
    {
        var templatePath = string.Format("hsr_relic_slot_{0}", slot);
        var templateImage = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(templatePath));
        return templateImage;
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
}
