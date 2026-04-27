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
    WeaponModuleData? Weapon = null);

public record CharacterModuleStyle(
    FontDefinitions Fonts,
    Color[] RarityColors,
    Color NameColor,
    Color LevelTextColor,
    Color LevelOverlayColor,
    Color NormalConstColor,
    Color GoldConstColor,
    Color GoldConstTextColor);

public class CharacterModuleRenderer
{
    private readonly CharacterModuleStyle m_Style;

    // Constants
    public static readonly Size CanvasSize = new(500, 200);
    private static readonly Size AvatarSize = new(150, 180);
    private static readonly Size WeaponSize = new(150, 180);
    private static readonly Point AvatarOffset = new(10, 10);
    private static readonly Point WeaponOffset = new(340, 10);
    private static readonly Color BorderColor = Color.FromRgb(65, 65, 65);
    private static readonly float BorderThickness = 2f;
    private static readonly int CornerRadius = 15;
    private static readonly int NameAreaX = 160;
    private static readonly int NameAreaWidth = 130;

    private static readonly Size LevelOverlaySize = new(150, 30);

    public CharacterModuleRenderer(CharacterModuleStyle style)
    {
        m_Style = style;
    }

    public void Render(IImageProcessingContext ctx, CharacterModuleData data, Point position)
    {
        var avatarPos = new Point(position.X + AvatarOffset.X, position.Y + AvatarOffset.Y);
        var weaponPos = new Point(position.X + WeaponOffset.X, position.Y + WeaponOffset.Y);

        DrawAvatar(ctx, data, avatarPos);

        if (data.Weapon != null)
        {
            DrawWeapon(ctx, data.Weapon, weaponPos);
        }

        DrawCharacterName(ctx, data.Name, position);

        // Rounded border
        var borderPath = ImageUtility.CreateRoundedRectanglePath(
                CanvasSize.Width - 2,
                CanvasSize.Height - 2,
                CornerRadius)
            .Translate(position.X + 1, position.Y + 1);
        ctx.Draw(BorderColor, BorderThickness, borderPath);
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
        var textX = basePosition.X + NameAreaX;
        var textWidth = NameAreaWidth;

        var fonts = new[] { m_Style.Fonts.Normal, m_Style.Fonts.Medium, m_Style.Fonts.Small, m_Style.Fonts.Tiny };
        Font? chosenFont = null;
        FontRectangle textSize = default;

        foreach (var font in fonts)
        {
            var measureOptions = new RichTextOptions(font)
            {
                Origin = new Vector2(textX, basePosition.Y),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                WrappingLength = textWidth
            };

            textSize = TextMeasurer.MeasureSize(name, measureOptions);
            if (textSize.Width <= textWidth)
            {
                chosenFont = font;
                break;
            }
        }

        chosenFont ??= fonts[^1];

        var drawOptions = new RichTextOptions(chosenFont)
        {
            Origin = new Vector2(textX + textWidth / 2, basePosition.Y + AvatarOffset.Y * 3),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            WrappingLength = textWidth
        };

        ctx.DrawText(drawOptions, name, m_Style.NameColor);
    }
}
