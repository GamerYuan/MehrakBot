#region

using System.Numerics;
using Mehrak.Application.Renderers.Extensions;
using Mehrak.Application.Utility;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Renderers;

public record WeaponModuleData(
    int Level,
    int Rarity,
    int? AffixLevel,
    Image WeaponImage);

public record CharacterModuleData(
    string Name,
    int Level,
    int Rarity,
    Image AvatarImage,
    int? ConstellationNum = 0,
    Image? Icon = null,
    WeaponModuleData? Weapon = null);

public record CharacterModuleStyle(
    FontDefinitions Fonts,
    Color[] RarityColors,
    Color NameColor,
    Color LevelTextColor,
    Color LevelOverlayColor,
    Color NormalConstColor,
    Color GoldConstColor,
    Color GoldConstTextColor,
    Color FooterTextColor,
    Image? PlaceholderWeaponIcon = null);

public class CharacterModuleRenderer
{
    private readonly CharacterModuleStyle m_Style;

    // Constants
    public static readonly Size CanvasSize = new(330, 250);
    private static readonly Size AvatarSize = new(150, 180);
    private static readonly Size WeaponSize = new(150, 180);
    private static readonly Point AvatarOffset = new(10, 60);
    private static readonly Point WeaponOffset = new(170, 60);
    private static readonly Point NameCenter = new(165, 30);
    private static readonly Color BorderColor = Color.FromRgb(120, 120, 120);
    private static readonly float BorderThickness = 2f;
    private static readonly int CornerRadius = 15;
    private static readonly int NameAreaHeight = 40;
    private static readonly int NameAreaWidth = 300;

    private static readonly Size LevelOverlaySize = new(150, 30);

    public CharacterModuleRenderer(CharacterModuleStyle style)
    {
        m_Style = style;
    }

    public void Render(IImageProcessingContext ctx, CharacterModuleData data, Point position, Color? borderColor = null)
    {
        var avatarPos = new Point(position.X + AvatarOffset.X, position.Y + AvatarOffset.Y);
        var weaponPos = new Point(position.X + WeaponOffset.X, position.Y + WeaponOffset.Y);

        DrawAvatar(ctx, data, avatarPos);

        if (data.Weapon != null)
        {
            DrawWeapon(ctx, data.Weapon, weaponPos);
        }
        else
        {
            var path = ImageUtility.CreateRoundedRectanglePath(WeaponSize.Width, WeaponSize.Height, 10)
                .Translate(weaponPos.X, weaponPos.Y);
            ctx.Fill(Color.FromRgb(69, 69, 69), path);
            if (m_Style.PlaceholderWeaponIcon != null)
            {
                var placeholderPos = new Point(weaponPos.X + (WeaponSize.Width - m_Style.PlaceholderWeaponIcon.Width) / 2,
                    weaponPos.Y + (WeaponSize.Height - m_Style.PlaceholderWeaponIcon.Height) / 2);
                ctx.DrawImage(m_Style.PlaceholderWeaponIcon, placeholderPos, 1f);
            }
        }

        DrawCharacterName(ctx, data.Name, position);

        // Rounded border
        var actualBorderColor = borderColor ?? BorderColor;
        var borderPath = ImageUtility.CreateRoundedRectanglePath(
                CanvasSize.Width - 2,
                CanvasSize.Height - 2,
                CornerRadius)
            .Translate(position.X + 1, position.Y + 1);
        ctx.Draw(actualBorderColor, BorderThickness, borderPath);
    }

    public Image<Rgba32> RenderFooterModule(string text, int count, Color borderColor, Image? icon = null)
    {
        const int padding = 10;
        const int gapBetweenLeftAndCount = 30;
        const int iconSize = 40;
        const int height = 70;

        var textSize = TextMeasurer.MeasureSize(text, new TextOptions(m_Style.Fonts.Normal));
        var countSize = TextMeasurer.MeasureSize(count.ToString(), new TextOptions(m_Style.Fonts.Normal));

        var leftWidth = icon != null
            ? padding + iconSize + padding + (int)textSize.Width
            : padding + (int)textSize.Width;
        var totalWidth = leftWidth + gapBetweenLeftAndCount + (int)countSize.Width + padding;

        Image<Rgba32> module = new(totalWidth, height);
        module.Mutate(ctx =>
        {
            ctx.Clear(Color.Transparent);

            var path = ImageUtility.CreateRoundedRectanglePath(totalWidth - 2, height - 2, 10).Translate(1, 1);
            ctx.Draw(borderColor, 2f, path);

            if (icon != null)
            {
                ctx.DrawImage(icon, new Point(padding, (height - iconSize) / 2), 1f);
            }

            var textX = icon != null
                ? padding + iconSize + padding + textSize.Width / 2
                : padding + textSize.Width / 2;

            ctx.DrawText(new RichTextOptions(m_Style.Fonts.Normal)
            {
                Origin = new Vector2(textX, height / 2),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }, text, m_Style.FooterTextColor);

            ctx.DrawText(new RichTextOptions(m_Style.Fonts.Normal)
            {
                Origin = new Vector2(totalWidth - padding, height / 2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            }, count.ToString(), m_Style.FooterTextColor);
        });
        return module;
    }

    private void DrawAvatar(IImageProcessingContext ctx, CharacterModuleData data, Point position)
    {
        var width = AvatarSize.Width;
        var height = AvatarSize.Height;

        using var temp = new Image<Rgba32>(width, height);
        temp.Mutate(tctx =>
        {
            var rectangle = new RectangleF(0, height - LevelOverlaySize.Height,
                LevelOverlaySize.Width, LevelOverlaySize.Height);
            var levelTextY = height - LevelOverlaySize.Height / 2;

            tctx.Fill(m_Style.RarityColors[data.Rarity - 1], new RectangleF(0, 0, width, height));
            tctx.DrawImage(data.AvatarImage, Point.Empty, 1f);

            // Level overlay
            var levelText = $"Lv. {data.Level}";
            var levelRect = TextMeasurer.MeasureSize(levelText, new TextOptions(m_Style.Fonts.Small!));
            tctx.Fill(m_Style.LevelOverlayColor, rectangle);
            tctx.DrawText(new RichTextOptions(m_Style.Fonts.Small!)
            {
                Origin = new Vector2((width - levelRect.Width) / 2, levelTextY - levelRect.Height / 2),
            }, levelText, m_Style.LevelTextColor);

            // Constellation
            if (data.ConstellationNum > 0)
            {
                tctx.DrawRoundedRectangleOverlay(30, 30, new PointF(width - 35, height - 65),
                    new RoundedRectangleOverlayStyle(
                        data.ConstellationNum == 6 ? m_Style.GoldConstColor : m_Style.NormalConstColor,
                        CornerRadius: 5));
                tctx.DrawText(new RichTextOptions(m_Style.Fonts.Normal)
                {
                    Origin = new Vector2(width - 20, height - 50),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                    data.ConstellationNum.ToString()!,
                    data.ConstellationNum == 6 ? m_Style.GoldConstTextColor : Color.White);
            }

            if (data.Icon != null)
            {
                tctx.DrawImage(data.Icon, new Point(5, 5), 1f);
            }

            tctx.ApplyRoundedCorners(10);
        });

        ctx.DrawImage(temp, position, 1f);
    }

    private void DrawWeapon(IImageProcessingContext ctx, WeaponModuleData weapon, Point position)
    {
        var width = WeaponSize.Width;
        var height = WeaponSize.Height;

        using var temp = new Image<Rgba32>(width, height);
        temp.Mutate(tctx =>
        {
            var rectangle = new RectangleF(0, height - LevelOverlaySize.Height,
                LevelOverlaySize.Width, LevelOverlaySize.Height);
            var levelTextY = height - LevelOverlaySize.Height / 2;

            tctx.Fill(m_Style.RarityColors[weapon.Rarity - 1], new RectangleF(0, 0, width, height));
            tctx.DrawImage(weapon.WeaponImage, Point.Empty, 1f);

            // Level overlay
            var levelText = $"Lv. {weapon.Level}";
            var levelRect = TextMeasurer.MeasureSize(levelText, new TextOptions(m_Style.Fonts.Small!));
            tctx.Fill(m_Style.LevelOverlayColor, rectangle);
            tctx.DrawText(new RichTextOptions(m_Style.Fonts.Small!)
            {
                Origin = new Vector2((width - levelRect.Width) / 2, levelTextY - levelRect.Height / 2),
            }, levelText, m_Style.LevelTextColor);

            // Affix level as const text
            if (weapon.AffixLevel > 0)
            {
                tctx.DrawRoundedRectangleOverlay(30, 30, new PointF(5, height - 65),
                    new RoundedRectangleOverlayStyle(
                        weapon.AffixLevel == 5 ? m_Style.GoldConstColor : m_Style.NormalConstColor,
                        CornerRadius: 5));
                tctx.DrawText(new RichTextOptions(m_Style.Fonts.Normal)
                {
                    Origin = new Vector2(20, height - 50),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                    weapon.AffixLevel.ToString()!,
                    weapon.AffixLevel == 5 ? m_Style.GoldConstTextColor : Color.White);
            }

            tctx.ApplyRoundedCorners(10);
        });

        ctx.DrawImage(temp, position, 1f);
    }

    private void DrawCharacterName(IImageProcessingContext ctx, string name, Point basePosition)
    {
        var fonts = new[] { m_Style.Fonts.Normal, m_Style.Fonts.Medium, m_Style.Fonts.Small, m_Style.Fonts.Tiny };
        Font? chosenFont = null;
        FontRectangle textSize = default;

        foreach (var font in fonts)
        {
            var measureOptions = new RichTextOptions(font)
            {
                Origin = Vector2.Zero,
                WrappingLength = NameAreaWidth
            };

            textSize = TextMeasurer.MeasureSize(name, measureOptions);
            var lineCount = TextMeasurer.CountLines(name, measureOptions);
            if ((lineCount == 1 && textSize.Width <= NameAreaWidth) ||
                (lineCount > 1 && textSize.Width <= NameAreaWidth && textSize.Height <= NameAreaHeight))
            {
                chosenFont = font;
                break;
            }
        }

        chosenFont ??= fonts[^1];

        if (chosenFont == fonts[^1])
        {
            var measureOptions = new RichTextOptions(chosenFont)
            {
                Origin = Vector2.Zero,
                WrappingLength = NameAreaWidth
            };
            textSize = TextMeasurer.MeasureSize(name, measureOptions);
        }

        var drawOptions = new RichTextOptions(chosenFont)
        {
            Origin = new Vector2(basePosition.X + NameCenter.X,
                basePosition.Y + NameCenter.Y - textSize.Height / 2),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            TextAlignment = TextAlignment.Center,
            WrappingLength = NameAreaWidth
        };

        ctx.DrawText(drawOptions, name, m_Style.NameColor);
    }
}
