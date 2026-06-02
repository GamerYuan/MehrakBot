#region

using System.Numerics;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Renderers;
using Mehrak.Application.Shared.Renderers.Extensions;
using Mehrak.Application.Shared.Utility;
using Mehrak.Domain.Character.Models;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Models;
using Mehrak.Domain.Shared.Utility;
using Mehrak.Domain.User.Abstractions;
using Mehrak.GameApi.Zzz.Types;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Zzz.Character;

internal class ZzzCharacterCardService : CardServiceBase<ZzzFullAvatarData>
{
    private Dictionary<string, Image> m_StatImages = [];
    private Dictionary<string, Image> m_DimmedStatImages = [];
    private Dictionary<int, Image> m_SkillImages = [];
    private Dictionary<string, Image> m_AttributeImages = [];
    private Dictionary<int, Image> m_ProfessionImages = [];
    private Dictionary<char, Image> m_RarityImages = [];
    private Dictionary<int, Image> m_WeaponStarImages = [];

    private readonly Font m_ExtraLargeFont;

    private static readonly Color LocalBackgroundColor = Color.FromPixel(new Rgb24(69, 69, 69));
    private static readonly Color LocalOverlayColor = Color.FromPixel(new Rgb24(36, 36, 36));

    private static readonly DrawingOptions RotateOptions = new()
    {
        Transform = new(Matrix3x2.CreateRotation(10 * MathF.PI / 180f))
    };

    public ZzzCharacterCardService(IImageRepository imageRepository,
        ILogger<ZzzCharacterCardService> logger,
        IApplicationMetrics metrics)
        : base(
            "Zzz Character",
            imageRepository,
            logger,
            metrics,
            LoadFonts("Assets/Fonts/zzz.ttf", titleSize: 64, normalSize: 40, mediumSize: 36, smallSize: 28, tinySize: 20))
    {
        m_ExtraLargeFont = new FontCollection().Add("Assets/Fonts/anton.ttf").CreateFont(400);
    }

    public override async Task LoadStaticResourcesAsync(CancellationToken cancellationToken = default)
    {
        var files = await ImageRepository.ListFilesAsync("zzz/stats_", cancellationToken);
        List<Task<(string x, Image)>> tasks =
        [
            .. files.Select(async file =>
                (file.Replace("zzz/stats_", "").Replace(".png", "").TrimStart(),
                    await Image.LoadAsync(await ImageRepository.DownloadFileToStreamAsync(file, cancellationToken), cancellationToken)))
        ];

        int[] skillIds = [0, 1, 2, 3, 5, 6];
        List<Task<(int x, Image)>> skillTasks =
        [
            .. skillIds.Select(async id =>
                (id, await Image.LoadAsync(await ImageRepository.DownloadFileToStreamAsync(
                    string.Format(FileNameFormat.Zzz.SkillName, id), cancellationToken), cancellationToken)))
        ];

        List<Task<(string x, Image)>> attributeTasks =
        [
            .. (await ImageRepository.ListFilesAsync("zzz/attribute_", cancellationToken)).Select(async file =>
                (file.Replace("zzz/attribute_", "").Replace(".png", "").TrimStart(),
                    await Image.LoadAsync(await ImageRepository.DownloadFileToStreamAsync(file, cancellationToken), cancellationToken)))
        ];

        List<Task<(int x, Image)>> professionTasks =
        [
            .. Enumerable.Range(1, 6).Select(async i =>
                (i, await Image.LoadAsync(await ImageRepository.DownloadFileToStreamAsync(
                    string.Format(FileNameFormat.Zzz.ProfessionName, i), cancellationToken), cancellationToken)))
        ];

        char[] itemRarity = ['s', 'a', 'b'];
        List<Task<(char x, Image)>> rarityTasks =
        [
            .. itemRarity.Select(async i =>
                (i, await Image.LoadAsync(await ImageRepository.DownloadFileToStreamAsync(
                    string.Format(FileNameFormat.Zzz.RarityName, i), cancellationToken), cancellationToken)))
        ];

        List<Task<(int x, Image)>> weaponStarTasks =
        [
            .. Enumerable.Range(1, 5)
                .Select(async i => (i, await Image.LoadAsync(
                    await ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.Zzz.WeaponStarName, i), cancellationToken), cancellationToken)))
        ];

        m_StatImages = (await Task.WhenAll(tasks)).ToDictionary(StringComparer.OrdinalIgnoreCase);
        m_DimmedStatImages = m_StatImages.ToDictionary(kv => kv.Key,
            kv => kv.Value.Clone(ctx => ctx.Brightness(0.5f)),
            StringComparer.OrdinalIgnoreCase);
        m_SkillImages = (await Task.WhenAll(skillTasks)).ToDictionary();
        m_AttributeImages = (await Task.WhenAll(attributeTasks)).ToDictionary();
        m_ProfessionImages = (await Task.WhenAll(professionTasks)).ToDictionary();
        m_RarityImages = (await Task.WhenAll(rarityTasks)).ToDictionary(CaseInsensitiveCharComparer.Instance);
        m_WeaponStarImages = (await Task.WhenAll(weaponStarTasks)).ToDictionary();
    }

    protected override Image<Rgba32> CreateBackground()
    {
        return new Image<Rgba32>(3000, 1200);
    }

    public override async Task RenderCardAsync(
        Image<Rgba32> background,
        ICardGenerationContext<ZzzFullAvatarData> context,
        DisposableBag disposables,
        CancellationToken cancellationToken = default)
    {
        var characterInformation = context.Data;
        var character = characterInformation.AvatarList[0];

        var portraitTask = LoadImageFromRepositoryAsync(
            character.ToImageName(), disposables, cancellationToken);
        var weaponTask = character.Weapon != null
            ? LoadImageFromRepositoryAsync(character.Weapon.ToImageName(), disposables, cancellationToken)
            : Task.FromResult<Image>(new Image<Rgba32>(1, 1));
        Task<(DiskDrive? Data, Image<Rgba32>? Image)>[] diskTasks =
        [
            .. Enumerable.Range(1, 6).Select(async i =>
            {
                var disk = character.Equip.FirstOrDefault(x => x.EquipmentType == i);
                if (disk != null)
                {
                    var diskImage = await LoadImageFromRepositoryAsync<Rgba32>(disk.ToImageName(), disposables, cancellationToken);
                    return (Data: disk, Image: diskImage);
                }
                return (Data: (DiskDrive?)null, Image: (Image<Rgba32>?)null);
            })
        ];

        var accentColor = Color.ParseHex(character.VerticalPaintingColor);

        var portraitImage = await portraitTask;

        var portraitConfig = context.GetParameter<CharacterPortraitConfig>("portraitConfig");
        portraitImage.Mutate(ctx =>
        {
            if (portraitConfig?.TargetScale > 0f)
            {
                var scale = portraitConfig.TargetScale.Value;
                ctx.Resize((int)(ctx.GetCurrentSize().Width * scale), 0, KnownResamplers.Lanczos3);
            }
            else
            {
                ctx.Resize(2000, 0, KnownResamplers.Lanczos3);
            }

            if (portraitConfig?.EnableGradientFade == true &&
                (portraitConfig?.GradientFadeStart ?? 0.75f) > 0f)
                ctx.ApplyGradientFade(portraitConfig?.GradientFadeStart ?? 0.75f);
        });

        var weaponImage = await weaponTask;
        (DiskDrive? Data, Image<Rgba32>? Image)[] diskSlots = [.. await Task.WhenAll(diskTasks)];

        background.Mutate(ctx =>
        {
            ctx.Paint(canvas =>
            {
                canvas.Fill(Brushes.Solid(accentColor), new Rectangle(0, 0, background.Width, background.Height));
            });

            ctx.Paint(canvas =>
            {
                canvas.DrawFauxItalicText(new RichTextOptions(m_ExtraLargeFont)
                {
                    Origin = new Vector2(-500, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    WrappingLength = 1800,
                    WordBreaking = WordBreaking.BreakAll
                }, string.Join($"{character.FullName.ToUpperInvariant()} ", Enumerable.Range(0, 5).Select(_ => "")),
                    Color.White.WithAlpha(0.25f));

                var offsetX = portraitConfig?.OffsetX ?? 0;
                var offsetY = portraitConfig?.OffsetY ?? 0;
                canvas.DrawImage(portraitImage, portraitImage.Bounds,
                    new RectangleF(350 - portraitImage.Width / 2 + offsetX, 650 - portraitImage.Height / 4 + offsetY,
                        portraitImage.Width, portraitImage.Height), KnownResamplers.Bicubic);

                canvas.DrawTextWithShadow(character.Name!, new RichTextOptions(Fonts.Title)
                {
                    Origin = new PointF(70, 50),
                    WrappingLength = 700,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                }, Color.White);

                var bounds = TextMeasurer.MeasureBounds(character.Name!, new RichTextOptions(Fonts.Title)
                {
                    Origin = new PointF(70, 50),
                    WrappingLength = 700,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                });

                canvas.DrawTextWithShadow($"Lv. {character.Level}", Fonts.Normal,
                    new PointF(70, bounds.Bottom + 10), Color.White);

                canvas.DrawTextWithShadow(context.GameProfile.Nickname, Fonts.Normal, new PointF(70, 1100), Color.White);
                canvas.DrawTextWithShadow(context.GameProfile.GameUid, Fonts.Small, new PointF(70, 1140), Color.White);

                canvas.Fill(Brushes.Solid(LocalBackgroundColor), new Polygon(new LinearLineSegment(new PointF(900, 0), new PointF(688, 1200),
                    new PointF(3000, 1200), new PointF(3000, 0))));

                foreach (var rank in character.Ranks)
                {
                    var yOffset = 130 * (rank.Id - 1);
                    DrawRankTemplateImage(canvas,
                        new Point(902 - (int)MathF.Round(yOffset * 0.1763f), yOffset),
                        rank.Id, rank.IsUnlocked, accentColor);
                }

                DrawRotatedIconImage(canvas,
                    new Point(932 - (int)MathF.Round(1050 * 0.1763f), 1047),
                    m_ProfessionImages[character.AvatarProfession], accentColor);

                DrawRotatedIconImage(canvas,
                    new Point(932 - (int)MathF.Round(920 * 0.1763f), 917),
                    m_AttributeImages[
                        StatUtils.GetElementNameFromId(character.ElementType, character.SubElementType)],
                    accentColor);

                foreach (var skill in character.Skills)
                {
                    var skillIndex = skill.SkillType == 6 ? 4 : skill.SkillType;
                    var yOffset = skillIndex >= 3 ? 130 : 0;
                    var xOffset = skillIndex % 3 * 120;
                    var skillImg = m_SkillImages[skill.SkillType];
                    canvas.DrawImage(skillImg, skillImg.Bounds,
                        new RectangleF(1030 + xOffset, 70 + yOffset, skillImg.Width, skillImg.Height),
                        KnownResamplers.Bicubic);
                    canvas.DrawCenteredTextInEllipse(
                        skill.Level.ToString(),
                        new PointF(1110 + xOffset, 150 + yOffset),
                        25,
                        new EllipseTextStyle(
                            Fonts.Small,
                            Color.White,
                            LocalOverlayColor,
                            accentColor,
                            4f));
                }

                canvas.DrawRoundedRectangleOverlay(450, 330, new PointF(950, 690),
                    new RoundedRectangleOverlayStyle(LocalOverlayColor, accentColor, 4f, 30));

                if (character.Weapon != null)
                {
                    canvas.DrawImage(weaponImage, weaponImage.Bounds,
                        new RectangleF(970, 710, weaponImage.Width, weaponImage.Height), KnownResamplers.Bicubic);
                    var rarityImg = m_RarityImages[character.Weapon.Rarity[0]];
                    canvas.DrawImage(rarityImg, rarityImg.Bounds,
                        new RectangleF(970, 720, rarityImg.Width, rarityImg.Height), KnownResamplers.Bicubic);
                    var weaponStarImg = m_WeaponStarImages[character.Weapon.Star];
                    canvas.DrawImage(weaponStarImg, weaponStarImg.Bounds,
                        new RectangleF(970, 820, weaponStarImg.Width, weaponStarImg.Height), KnownResamplers.Bicubic);

                    canvas.DrawText(new RichTextOptions(Fonts.Medium!) { Origin = new PointF(980, 880) },
                        $"Lv.{character.Weapon.Level}", Brushes.Solid(Color.White), null);

                    canvas.DrawText(new RichTextOptions(character.Weapon.Name.Length > 40 ? Fonts.Small : Fonts.Medium)
                    {
                        Origin = new Vector2(1120, 730),
                        WrappingLength = 280,
                        VerticalAlignment = VerticalAlignment.Top
                    }, character.Weapon.Name, Brushes.Solid(Color.White), null);
                    var mainPropImg =
                        m_StatImages[StatUtils.GetStatAssetName(character.Weapon.MainProperties[0].PropertyName)];
                    canvas.DrawImage(mainPropImg, mainPropImg.Bounds,
                        new RectangleF(980, 930, mainPropImg.Width, mainPropImg.Height), KnownResamplers.Bicubic);
                    canvas.DrawText(new RichTextOptions(Fonts.Medium!) { Origin = new PointF(1030, 935) },
                        character.Weapon.MainProperties[0].Base, Brushes.Solid(Color.White), null);

                    var subPropImg =
                        m_StatImages[StatUtils.GetStatAssetName(character.Weapon.Properties[0].PropertyName)];
                    canvas.DrawImage(subPropImg, subPropImg.Bounds,
                        new RectangleF(1175, 930, subPropImg.Width, subPropImg.Height), KnownResamplers.Bicubic);
                    canvas.DrawText(new RichTextOptions(Fonts.Medium!) { Origin = new PointF(1225, 935) },
                        character.Weapon.Properties[0].Base, Brushes.Solid(Color.White), null);
                }
                else
                {
                    canvas.DrawText(new RichTextOptions(Fonts.Medium!)
                    {
                        Origin = new Vector2(1175, 848),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                        WrappingLength = 350
                    }, "No W-Engine Equipped", Brushes.Solid(Color.White), null);
                }

                canvas.DrawRoundedRectangleOverlay(700, 970, new PointF(1420, 50),
                    new RoundedRectangleOverlayStyle(LocalOverlayColor, accentColor, 4f, 30));

                var offsetInterval = 880 / (character.Properties.Count - 1);
                var statsYOffset = 0;

                for (var i = 0; i < character.Properties.Count; i++)
                {
                    var stat = character.Properties[i];

                    canvas.DrawStatLine(
                        new StatLineData(
                            stat.PropertyName,
                            stat.Final!,
                            stat.Base,
                            $"+{stat.Add}"),
                        new StatLineStyle(
                            m_StatImages.GetValueOrDefault(StatUtils.GetStatAssetName(stat.PropertyName)),
                            Fonts.Medium!,
                            Color.White,
                            Fonts.Tiny!,
                            Color.LightSlateGrey,
                            Color.LightGreen),
                        new PointF(1440, 75 + statsYOffset),
                        660);

                    statsYOffset += offsetInterval;
                }

                EquipSuit[] activeSets =
                    [.. character.Equip.Select(x => x.EquipSuit).DistinctBy(x => x.SuitId).Where(x => x.Own >= 2)];
                for (var i = 0; i < activeSets.Length; i++)
                {
                    var yOffset = i * 50;
                    var set = activeSets[i];
                    canvas.DrawText(new RichTextOptions(Fonts.Small)
                    {
                        Origin = new Vector2(2100, 1050 + yOffset),
                        HorizontalAlignment = HorizontalAlignment.Right
                    }, $"{set.Name}\tx{set.Own}", Brushes.Solid(Color.White), null);
                }

                if (activeSets.Length == 0)
                    canvas.DrawText(new RichTextOptions(Fonts.Small)
                    {
                        Origin = new Vector2(2100, 1050),
                        HorizontalAlignment = HorizontalAlignment.Right
                    }, "No Active Set", Brushes.Solid(Color.White), null);

                for (var i = 0; i < diskSlots.Length; i++)
                {
                    var slot = diskSlots[i];
                    if (slot.Data != null)
                    {
                        DrawDiskImage(canvas, new Point(2150, 50 + i * 186), slot.Data, slot.Image!);
                    }
                    else
                    {
                        DrawDiskTemplateImage(canvas, new Point(2150, 50 + i * 186));
                    }
                }
            });
        });
    }

    private void DrawDiskTemplateImage(DrawingCanvas canvas, Point position)
    {
        using var region = canvas.CreateRegion(new Rectangle(position, new Size(800, 170)));
        var clipPath = new RoundedRectanglePolygon(new RectangleF(Point.Empty, new Size(800, 170)), 30);
        _ = region.Save(ClipOptions, clipPath);
        region.Fill(Brushes.Solid(LocalOverlayColor));
        region.Restore();
        region.DrawText(new RichTextOptions(Fonts.Normal)
        {
            Origin = new Vector2(425, 78),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        }, "Not Equipped", Brushes.Solid(Color.White), null);
    }

    private void DrawDiskImage(DrawingCanvas canvas, Point position, DiskDrive disk, Image diskImage)
    {
        using var region = canvas.CreateRegion(new Rectangle(position, new Size(800, 170)));
        var clipPath = new RoundedRectanglePolygon(new RectangleF(Point.Empty, new Size(800, 170)), 30);
        _ = region.Save(ClipOptions, clipPath);
        region.Fill(Brushes.Solid(LocalOverlayColor));
        region.DrawImage(diskImage, diskImage.Bounds,
            new RectangleF(10, 15, diskImage.Width, diskImage.Height), KnownResamplers.Bicubic);

        region.Restore();

        var rarityImg = m_RarityImages[disk.Rarity[0]];
        region.DrawImage(rarityImg, rarityImg.Bounds,
            new RectangleF(20, 115, rarityImg.Width, rarityImg.Height), KnownResamplers.Bicubic);
        var mainStatImg = m_StatImages[StatUtils.GetStatAssetName(disk.MainProperties[0].PropertyName)];
        region.DrawImage(mainStatImg, mainStatImg.Bounds,
            new RectangleF(215, 20, mainStatImg.Width, mainStatImg.Height), KnownResamplers.Bicubic);
        region.DrawText(new RichTextOptions(Fonts.Normal)
        {
            Origin = new PointF(265, 70),
            HorizontalAlignment = HorizontalAlignment.Right
        }, disk.MainProperties[0]!.Base!, Brushes.Solid(Color.White), null);
        region.DrawText(new RichTextOptions(Fonts.Small)
        {
            Origin = new PointF(265, 120),
            HorizontalAlignment = HorizontalAlignment.Right
        }, $"Lv.{disk.Level}", Brushes.Solid(Color.White), null);

        for (var i = 0; i < disk.Properties!.Count; i++)
        {
            var subStat = disk.Properties[i];
            var xOffset = i % 2 * 260;
            var yOffset = i / 2 * 85;
            var color = Color.White;
            if (subStat is { PropertyName: "ATK" or "DEF" or "HP" } && !subStat.Base.EndsWith('%'))
            {
                var dim = m_DimmedStatImages[StatUtils.GetStatAssetName(subStat.PropertyName)];
                region.DrawImage(dim, dim.Bounds,
                    new RectangleF(280 + xOffset, 20 + yOffset, dim.Width, dim.Height), KnownResamplers.Bicubic);
                color = Color.FromPixel(new Rgb24(128, 128, 128));
            }
            else
            {
                var subStatImage = m_StatImages[StatUtils.GetStatAssetName(subStat.PropertyName)];
                region.DrawImage(subStatImage, subStatImage.Bounds,
                    new RectangleF(280 + xOffset, 20 + yOffset, subStatImage.Width, subStatImage.Height),
                    KnownResamplers.Bicubic);
            }

            region.DrawText(new RichTextOptions(Fonts.Normal) { Origin = new PointF(335 + xOffset, 23 + yOffset) },
                subStat.Base!, Brushes.Solid(color), null);
            var rolls = string.Concat(Enumerable.Repeat('.', subStat.Level));
            region.DrawText(new RichTextOptions(Fonts.Normal) { Origin = new PointF(460 + xOffset, 8 + yOffset) },
                rolls, Brushes.Solid(color), null);
        }
    }

    private void DrawRankTemplateImage(DrawingCanvas canvas, Point location, int rank, bool activated, Color accentColor)
    {
        using var region = canvas.CreateRegion(new Rectangle(location, new Size(150, 180)));
        _ = region.Save(RotateOptions);
        _ = region.SaveLayer();

        if (activated)
        {
            region.DrawRoundedRectangleOverlay(90, 120, new PointF(30, 15),
                new RoundedRectangleOverlayStyle(LocalOverlayColor, accentColor, 4f, 10));

            region.DrawText(new RichTextOptions(Fonts.Title)
            {
                Origin = new Vector2(75, 75),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }, rank.ToString("D2"), Brushes.Solid(Color.White), null);
        }
        else
        {
            var dimmedOverlay = (LocalOverlayColor.ToScaledVector4() * 0.5f).AsVector3();
            var dimmedAccent = (accentColor.ToScaledVector4() * 0.5f).AsVector3();
            region.DrawRoundedRectangleOverlay(90, 120, new PointF(30, 15),
                new RoundedRectangleOverlayStyle(Color.FromPixel(new Rgba32(dimmedOverlay)),
                Color.FromPixel(new Rgba32(dimmedAccent)), 4f, 10));

            region.DrawText(new RichTextOptions(Fonts.Title)
            {
                Origin = new Vector2(75, 75),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }, rank.ToString("D2"), Brushes.Solid(Color.FromPixel(new Rgb24(128, 128, 128))), null);
        }

        region.Restore();
        region.Restore();
    }

    private static void DrawRotatedIconImage(DrawingCanvas canvas, Point location, Image icon, Color accentColor)
    {
        using var region = canvas.CreateRegion(new Rectangle(location, new Size(90, 120)));
        _ = region.Save(RotateOptions);
        region.DrawRoundedRectangleOverlay(90, 120, PointF.Empty,
            new RoundedRectangleOverlayStyle(LocalOverlayColor, accentColor, 4f, 10));
        region.DrawImage(icon, icon.Bounds,
            new RectangleF(45 - icon.Width / 2, 60 - icon.Height / 2, icon.Width, icon.Height),
            KnownResamplers.Bicubic);
        region.Restore();
    }
}
