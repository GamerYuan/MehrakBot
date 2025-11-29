#region

using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Domain.Utility;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Services.Zzz.Character;

internal class ZzzCharacterCardService : ICardService<ZzzFullAvatarData>, IAsyncInitializable
{
    private readonly IImageRepository m_ImageRepository;
    private readonly ILogger<ZzzCharacterCardService> m_Logger;

    private Dictionary<string, Image> m_StatImages = [];
    private Dictionary<int, Image> m_SkillImages = [];
    private Dictionary<string, Image> m_AttributeImages = [];
    private Dictionary<int, Image> m_ProfessionImages = [];
    private Dictionary<char, Image> m_RarityImages = [];
    private Dictionary<int, Image> m_WeaponStarImages = [];

    private readonly Font m_SmallFont;
    private readonly Font m_NormalFont;
    private readonly Font m_MediumFont;
    private readonly Font m_TitleFont;
    private readonly Font m_ExtraLargeFont;
    private readonly Font m_VerySmallFont;

    private readonly JpegEncoder m_JpegEncoder;

    private readonly Image m_WeaponTemplate;
    private readonly Image m_DiskBackground;
    private readonly Image<Rgba32> m_DiskTemplate;

    private static readonly Color BackgroundColor = Color.FromRgb(69, 69, 69);
    private static readonly Color OverlayColor = Color.FromRgb(36, 36, 36);

    public ZzzCharacterCardService(IImageRepository imageRepository, ILogger<ZzzCharacterCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_Logger = logger;

        FontFamily fontFamily = new FontCollection().Add("Assets/Fonts/zzz.ttf");

        m_ExtraLargeFont = new FontCollection().Add("Assets/Fonts/anton.ttf").CreateFont(400);
        m_TitleFont = fontFamily.CreateFont(64);
        m_NormalFont = fontFamily.CreateFont(40);
        m_MediumFont = fontFamily.CreateFont(36);
        m_SmallFont = fontFamily.CreateFont(28);
        m_VerySmallFont = fontFamily.CreateFont(20);

        m_JpegEncoder = new JpegEncoder
        {
            Quality = 90,
            Interleaved = false
        };

        m_WeaponTemplate = new Image<Rgba32>(100, 100);

        m_DiskBackground = new Image<Rgba32>(800, 170);
        m_DiskBackground.Mutate(ctx =>
        {
            ctx.Fill(OverlayColor);
            ctx.ApplyRoundedCorners(30);
        });
        m_DiskTemplate = CreateDiskTemplateImage();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        List<string> files = await m_ImageRepository.ListFilesAsync("zzz_stats");
        List<Task<(string x, Image)>> tasks =
        [
            .. files.Select(async file =>
                (file.Replace("zzz_stats_", "").TrimStart(),
                    await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(file))))
        ];

        int[] skillIds = [0, 1, 2, 3, 5, 6];
        List<Task<(int x, Image)>> skillTasks =
        [
            .. skillIds.Select(async id =>
                (id, await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(
                    string.Format(FileNameFormat.Zzz.SkillName, id)))))
        ];

        List<Task<(string x, Image)>> attributeTasks =
        [
            .. (await m_ImageRepository.ListFilesAsync("zzz_attribute")).Select(async file =>
                (file.Replace("zzz_attribute_", "").TrimStart(),
                    await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(file))))
        ];

        List<Task<(int x, Image)>> professionTasks =
        [
            .. Enumerable.Range(1, 6).Select(async i =>
                (i, await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(
                    string.Format(FileNameFormat.Zzz.ProfessionName, i)))))
        ];

        char[] itemRarity = ['s', 'a', 'b'];
        List<Task<(char x, Image)>> rarityTasks =
        [
            .. itemRarity.Select(async i =>
                (i, await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(
                    string.Format("zzz_rarity_{0}", i)))))
        ];

        List<Task<(int x, Image)>> weaponStarTasks =
        [
            .. Enumerable.Range(1, 5)
                .Select(async i => (i, await Image.LoadAsync(
                    await m_ImageRepository.DownloadFileToStreamAsync($"zzz_weapon_star_{i}"))))
        ];

        m_StatImages = (await Task.WhenAll(tasks)).ToDictionary(StringComparer.OrdinalIgnoreCase);
        m_SkillImages = (await Task.WhenAll(skillTasks)).ToDictionary();
        m_AttributeImages = (await Task.WhenAll(attributeTasks)).ToDictionary();
        m_ProfessionImages = (await Task.WhenAll(professionTasks)).ToDictionary();
        m_RarityImages = (await Task.WhenAll(rarityTasks)).ToDictionary(CaseInsensitiveCharComparer.Instance);
        m_WeaponStarImages = (await Task.WhenAll(weaponStarTasks)).ToDictionary();

        m_Logger.LogInformation(LogMessage.ServiceInitialized, nameof(ZzzCharacterCardService));
    }

    public async Task<Stream> GetCardAsync(ICardGenerationContext<ZzzFullAvatarData> context)
    {
        m_Logger.LogInformation(LogMessage.CardGenStartInfo, "Character", context.UserId);
        var stopwatch = Stopwatch.StartNew();

        List<IDisposable> disposables = [];

        ZzzFullAvatarData characterInformation = context.Data;
        try
        {
            ZzzAvatarData character = characterInformation.AvatarList[0];

            Image<Rgba32> background = new(3000, 1200);

            Task<Image> portraitTask = Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(
                string.Format(character.ToImageName())));
            Task<Image> weaponTask = character.Weapon == null
                ? Task.FromResult(m_WeaponTemplate.Clone(ctx => { }))
                : Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(
                    string.Format(character.Weapon.ToImageName())));
            List<Image> diskImage = await Enumerable.Range(1, 6).ToAsyncEnumerable()
                .Select(async (i, token) =>
                {
                    DiskDrive? disk = character.Equip.FirstOrDefault(x => x.EquipmentType == i);
                    if (disk == null)
                        return m_DiskTemplate.CloneAs<Rgba32>();
                    else
                        return await CreateDiskImageAsync(disk, token);
                }).ToListAsync();

            var accentColor = Color.ParseHex(character.VerticalPaintingColor);

            Image portraitImage = await portraitTask;
            Image weaponImage = await weaponTask;
            disposables.Add(portraitImage);
            disposables.Add(weaponImage);
            disposables.AddRange(diskImage);

            background.Mutate(ctx =>
            {
                ctx.Clear(accentColor);

                // Character Overview
                ctx.DrawFauxItalicText(new RichTextOptions(m_ExtraLargeFont)
                {
                    Origin = new Vector2(-500, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    WrappingLength = 1800,
                    WordBreaking = WordBreaking.BreakAll
                }, string.Join($"{character.FullName.ToUpperInvariant()} ", Enumerable.Range(0, 5).Select(_ => "")),
                    Color.White.WithAlpha(0.25f));

                ctx.DrawImage(portraitImage,
                    new Point(350 - portraitImage.Width / 2, 650 - portraitImage.Height / 4), 1f);

                ctx.DrawText(new RichTextOptions(m_TitleFont)
                {
                    Origin = new PointF(73, 53),
                    WrappingLength = 700,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                }, character.Name!, Color.Black);
                ctx.DrawText(new RichTextOptions(m_TitleFont)
                {
                    Origin = new PointF(70, 50),
                    WrappingLength = 700,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                }, character.Name!, Color.White);

                FontRectangle bounds = TextMeasurer.MeasureBounds(character.Name!, new RichTextOptions(m_TitleFont)
                {
                    Origin = new PointF(70, 50),
                    WrappingLength = 700,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                });

                ctx.DrawText($"Lv. {character.Level}", m_NormalFont, Color.Black,
                    new PointF(73, bounds.Bottom + 23));
                ctx.DrawText($"Lv. {character.Level}", m_NormalFont, Color.White,
                    new PointF(70, bounds.Bottom + 20));
                ctx.DrawText(context.GameProfile.GameUid, m_SmallFont, Color.White, new PointF(70, 1150));

                ctx.FillPolygon(BackgroundColor, new PointF(900, 0), new PointF(688, 1200), new PointF(3000, 1200),
                    new PointF(3000, 0));

                foreach (Rank rank in character.Ranks)
                {
                    using Image<Rgba32> rankImage = CreateRankTemplateImage(rank.Id, rank.IsUnlocked, accentColor);
                    var yOffset = 130 * (rank.Id - 1);
                    ctx.DrawImage(rankImage, new Point(890 - (int)MathF.Round(yOffset * 0.1763f), yOffset), 1f);
                }

                using Image<Rgba32> professionImage =
                    CreateRotatedIconImage(m_ProfessionImages[character.AvatarProfession], accentColor);
                ctx.DrawImage(professionImage, new Point(890 - (int)MathF.Round(1030 * 0.1763f), 1030), 1f);

                using Image<Rgba32> elementImage =
                    CreateRotatedIconImage(
                        m_AttributeImages[
                            StatUtils.GetElementNameFromId(character.ElementType, character.SubElementType)],
                        accentColor);
                ctx.DrawImage(elementImage, new Point(890 - (int)MathF.Round(900 * 0.1763f), 900), 1f);

                // Skill
                foreach (Skill skill in character.Skills)
                {
                    var skillIndex = skill.SkillType == 6 ? 4 : skill.SkillType;
                    var yOffset = skillIndex >= 3 ? 130 : 0;
                    var xOffset = skillIndex % 3 * 120;
                    ctx.DrawImage(m_SkillImages[skill.SkillType],
                        new Point(1030 + xOffset, 70 + yOffset), 1f);
                    EllipsePolygon skillEllipse = new(new PointF(1110 + xOffset, 150 + yOffset), 25);
                    ctx.Fill(OverlayColor, skillEllipse.AsClosedPath());
                    ctx.DrawText(new RichTextOptions(m_SmallFont)
                    {
                        Origin = new Vector2(1110 + xOffset, 157 + yOffset),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }, skill.Level.ToString(), Color.White);
                    ctx.Draw(accentColor, 4f, skillEllipse.AsClosedPath());
                }

                IPath weaponModule = ImageUtility.CreateRoundedRectanglePath(450, 330, 30).Translate(950, 690);
                ctx.Fill(OverlayColor, weaponModule);
                ctx.Draw(accentColor, 4f, weaponModule);

                // Weapon
                if (character.Weapon != null)
                {
                    ctx.DrawImage(weaponImage, new Point(970, 710), 1f);
                    ctx.DrawImage(m_RarityImages[character.Weapon.Rarity[0]], new Point(970, 720), 1f);
                    ctx.DrawImage(m_WeaponStarImages[character.Weapon.Star], new Point(970, 820), 1f);

                    ctx.DrawText($"Lv.{character.Weapon.Level}", m_MediumFont, Color.White, new PointF(980, 890));

                    ctx.DrawText(new RichTextOptions(character.Weapon.Name.Length > 40 ? m_SmallFont : m_MediumFont)
                    {
                        Origin = new Vector2(1120, 740),
                        WrappingLength = 280,
                        VerticalAlignment = VerticalAlignment.Top
                    }, character.Weapon.Name, Color.White);
                    ctx.DrawImage(
                        m_StatImages[StatUtils.GetStatAssetName(character.Weapon.MainProperties[0].PropertyName)],
                        new Point(980, 940), 1f);
                    ctx.DrawText(character.Weapon.MainProperties[0].Base, m_MediumFont, Color.White,
                        new PointF(1030, 955));

                    ctx.DrawImage(m_StatImages[StatUtils.GetStatAssetName(character.Weapon.Properties[0].PropertyName)],
                        new Point(1175, 940), 1f);
                    ctx.DrawText(character.Weapon.Properties[0].Base, m_MediumFont, Color.White, new PointF(1225, 955));
                }
                else
                {
                    ctx.DrawText(new RichTextOptions(m_MediumFont)
                    {
                        Origin = new Vector2(1175, 858),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                        WrappingLength = 350
                    }, "No W-Engine Equipped", Color.White);
                }

                // Stats
                IPath statsModule = ImageUtility.CreateRoundedRectanglePath(700, 970, 30).Translate(1420, 50);
                ctx.Fill(OverlayColor, statsModule);
                ctx.Draw(accentColor, 4f, statsModule);

                var offsetInterval = 880 / (character.Properties.Count - 1);
                var statsYOffset = 0;

                for (var i = 0; i < character.Properties.Count; i++)
                {
                    CharacterProperty stat = character.Properties[i];

                    ctx.DrawImage(m_StatImages[StatUtils.GetStatAssetName(stat.PropertyName)],
                        new Point(1440, 75 + statsYOffset), 1f);

                    if (stat.PropertyName.Length > 20)
                        ctx.DrawText(new RichTextOptions(m_SmallFont)
                        {
                            Origin = new Vector2(1495, 103 + statsYOffset),
                            VerticalAlignment = VerticalAlignment.Center,
                            WrappingLength = 620
                        }, stat.PropertyName, Color.White);
                    else
                        ctx.DrawText(stat.PropertyName, m_MediumFont, Color.White, new PointF(1495, 90 + statsYOffset));

                    ctx.DrawText(new RichTextOptions(m_MediumFont)
                    {
                        Origin = new Vector2(2100, 90 + statsYOffset),
                        HorizontalAlignment = HorizontalAlignment.Right
                    }, stat.Final!, Color.White);

                    if (!string.IsNullOrEmpty(stat.Base))
                    {
                        RichTextOptions option = new(m_VerySmallFont)
                        {
                            Origin = new Vector2(2100, 125 + statsYOffset),
                            HorizontalAlignment = HorizontalAlignment.Right
                        };
                        var addText = $"+{stat.Add}";
                        FontRectangle addBound = TextMeasurer.MeasureBounds(addText, option);
                        ctx.DrawText(option, addText, Color.LightGreen);

                        ctx.DrawText(new RichTextOptions(m_VerySmallFont)
                        {
                            Origin = new Vector2(addBound.Left - 5, 125 + statsYOffset),
                            HorizontalAlignment = HorizontalAlignment.Right
                        }, stat.Base, Color.LightSlateGrey);
                    }

                    statsYOffset += offsetInterval;
                }

                // Active Set
                EquipSuit[] activeSets =
                    [.. character.Equip.Select(x => x.EquipSuit).DistinctBy(x => x.SuitId).Where(x => x.Own >= 2)];
                for (var i = 0; i < activeSets.Length; i++)
                {
                    var yOffset = i * 50;
                    EquipSuit set = activeSets[i];
                    ctx.DrawText(new RichTextOptions(m_SmallFont)
                    {
                        Origin = new Vector2(2100, 1060 + yOffset),
                        HorizontalAlignment = HorizontalAlignment.Right
                    }, $"{set.Name}\tx{set.Own}", Color.White);
                }

                if (activeSets.Length == 0)
                    ctx.DrawText(new RichTextOptions(m_SmallFont)
                    {
                        Origin = new Vector2(2100, 1060),
                        HorizontalAlignment = HorizontalAlignment.Right
                    }, "No Active Set", Color.White);

                for (var i = 0; i < diskImage.Count; i++)
                {
                    var offset = i * 186;
                    ctx.DrawImage(diskImage[i], new Point(2150, 50 + offset), 1f);
                }
            });

            MemoryStream stream = new();
            await background.SaveAsJpegAsync(stream, m_JpegEncoder);
            stream.Position = 0;

            m_Logger.LogInformation(LogMessage.CardGenSuccess, "Character", context.UserId,
                stopwatch.ElapsedMilliseconds);
            return stream;
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessage.CardGenError, "Character", context.UserId,
                JsonSerializer.Serialize(characterInformation));
            throw new CommandException("Failed to generate Character card", e);
        }
        finally
        {
            disposables.ForEach(d => d.Dispose());
        }
    }

    private Image<Rgba32> CreateDiskTemplateImage()
    {
        Image<Rgba32> diskTemplate = m_DiskBackground.CloneAs<Rgba32>();

        diskTemplate.Mutate(ctx =>
        {
            ctx.DrawText(new RichTextOptions(m_NormalFont)
            {
                Origin = new Vector2(425, 88),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            }, "Not Equipped", Color.White);
        });

        return diskTemplate;
    }

    private async ValueTask<Image> CreateDiskImageAsync(DiskDrive disk, CancellationToken token = default)
    {
        Image diskImage = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(
            disk.ToImageName()), token);
        Image<Rgba32> diskTemplate = m_DiskBackground.CloneAs<Rgba32>();
        diskTemplate.Mutate(ctx =>
        {
            ctx.DrawImage(diskImage, new Point(10, 15), 1f);
            ctx.DrawImage(m_RarityImages[disk.Rarity[0]], new Point(20, 115), 1f);
            ctx.DrawImage(m_StatImages[StatUtils.GetStatAssetName(disk.MainProperties[0].PropertyName)],
                new Point(215, 20), 1f);
            ctx.DrawText(new RichTextOptions(m_NormalFont)
            {
                Origin = new PointF(265, 80),
                HorizontalAlignment = HorizontalAlignment.Right
            }, disk.MainProperties[0]!.Base!, Color.White);
            ctx.DrawText(new RichTextOptions(m_SmallFont)
            {
                Origin = new PointF(265, 130),
                HorizontalAlignment = HorizontalAlignment.Right
            }, $"Lv.{disk.Level}", Color.White);
            // Draw properties
            for (var i = 0; i < disk.Properties!.Count; i++)
            {
                EquipProperty subStat = disk.Properties[i];
                Image subStatImage = m_StatImages[StatUtils.GetStatAssetName(subStat.PropertyName)];
                var xOffset = i % 2 * 260;
                var yOffset = i / 2 * 85;
                Color color = Color.White;
                if (subStat is { PropertyName: "ATK" or "DEF" or "HP" } && !subStat.Base.EndsWith('%'))
                {
                    Image<Rgba32> dim = subStatImage.CloneAs<Rgba32>();
                    dim.Mutate(x => x.Brightness(0.5f));
                    ctx.DrawImage(dim, new Point(280 + xOffset, 20 + yOffset), 1f);
                    color = Color.FromRgb(128, 128, 128);
                }
                else
                {
                    ctx.DrawImage(subStatImage, new Point(280 + xOffset, 20 + yOffset), 1f);
                }

                ctx.DrawText(subStat.Base!, m_NormalFont, color, new PointF(335 + xOffset, 33 + yOffset));
                var rolls = string.Concat(Enumerable.Repeat('.', subStat.Level));
                ctx.DrawText(rolls, m_NormalFont, color, new PointF(460 + xOffset, 18 + yOffset));
            }
        });
        return diskTemplate;
    }

    private Image<Rgba32> CreateRankTemplateImage(int rank, bool activated, Color accentColor)
    {
        Image<Rgba32> image = new(120, 150);

        image.Mutate(ctx =>
        {
            ctx.Clear(Color.Transparent);

            if (!activated) ctx.Brightness(2f);

            IPath path = ImageUtility.CreateRoundedRectanglePath(90, 120, 10).Translate(15, 15);
            ctx.Fill(OverlayColor, path);
            ctx.Draw(accentColor, 4f, path);
            ctx.DrawText(new RichTextOptions(m_TitleFont)
            {
                Origin = new Vector2(60, 90),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }, rank.ToString("D2"), Color.White);
            if (!activated) ctx.Brightness(0.5f);

            ctx.Rotate(10, KnownResamplers.Bicubic);
        });
        return image;
    }

    private static Image<Rgba32> CreateRotatedIconImage(Image icon, Color accentColor)
    {
        Image<Rgba32> image = new(120, 150);

        image.Mutate(ctx =>
        {
            ctx.Clear(Color.Transparent);
            IPath path = ImageUtility.CreateRoundedRectanglePath(90, 120, 10).Translate(15, 15);
            ctx.Fill(OverlayColor, path);
            ctx.Draw(accentColor, 4f, path);
            ctx.DrawImage(icon, new Point(60 - icon.Width / 2, 75 - icon.Height / 2), 1f);
            ctx.Rotate(10, KnownResamplers.Bicubic);
        });

        return image;
    }
}
