#region

using System.Numerics;
using Mehrak.Application.Shared.Renderers.Extensions;
using Mehrak.Application.Shared.Utility;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Shared.Renderers;

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
    Image? PlaceholderWeaponIcon = null,
    bool DrawWeapon = true,
    Color? AvatarBorderColor = null);

public class CharacterModuleRenderer
{
    private readonly CharacterModuleStyle m_Style;

    // Constants
    public static readonly Size DefaultCanvasSize = new(330, 250);
    public static readonly Size NoWeaponCanvasSize = new(170, 250);
    public Size CanvasSize => m_Style.DrawWeapon ? DefaultCanvasSize : NoWeaponCanvasSize;

    private static readonly Size AvatarSize = new(150, 180);
    private static readonly Size WeaponSize = new(150, 180);
    private static readonly Point AvatarOffset = new(10, 60);
    private static readonly Point WeaponOffset = new(170, 60);
    private static readonly Point NameCenter = new(165, 30);
    private static readonly Point NoWeaponNameCenter = new(85, 30);
    private static readonly Color BorderColor = Color.FromPixel(new Rgba32(120, 120, 120));
    private static readonly float BorderThickness = 2f;
    private static readonly int CornerRadius = 15;
    private static readonly int NameAreaHeight = 40;
    private static readonly int NameAreaWidth = 300;
    private static readonly int NoWeaponNameAreaWidth = 150;

    private static readonly Size LevelOverlaySize = new(150, 30);

    private static readonly DrawingOptions ClipOptions = new()
    {
        ShapeOptions = new ShapeOptions()
        {
            BooleanOperation = BooleanOperation.Intersection,
        }
    };

    public CharacterModuleRenderer(CharacterModuleStyle style)
    {
        m_Style = style;
    }

    public void Render(DrawingCanvas canvas, CharacterModuleData data, Point position, Color? borderColor = null)
    {
        var canvasSize = CanvasSize;
        var avatarPos = new Point(position.X + AvatarOffset.X, position.Y + AvatarOffset.Y);
        _ = canvas.SaveLayer();
        DrawAvatar(canvas, data, avatarPos);

        if (m_Style.DrawWeapon)
        {
            var weaponPos = new Point(position.X + WeaponOffset.X, position.Y + WeaponOffset.Y);
            if (data.Weapon != null)
            {
                DrawWeapon(canvas, data.Weapon, weaponPos);
            }
            else
            {
                _ = canvas.SaveLayer();
                var path = new RoundedRectanglePolygon(new RectangleF(weaponPos.X, weaponPos.Y, WeaponSize.Width, WeaponSize.Height), 10);
                canvas.Fill(Brushes.Solid(Color.FromPixel(new Rgba32(69, 69, 69))), path);
                if (m_Style.PlaceholderWeaponIcon != null)
                {
                    var placeholderPos = new Point(weaponPos.X + (WeaponSize.Width - m_Style.PlaceholderWeaponIcon.Width) / 2,
                        weaponPos.Y + (WeaponSize.Height - m_Style.PlaceholderWeaponIcon.Height) / 2);
                    canvas.DrawImage(m_Style.PlaceholderWeaponIcon, m_Style.PlaceholderWeaponIcon.Bounds,
                        new RectangleF(placeholderPos.X, placeholderPos.Y, m_Style.PlaceholderWeaponIcon.Width, m_Style.PlaceholderWeaponIcon.Height),
                        KnownResamplers.Bicubic);
                }
                canvas.Restore();
            }
        }

        DrawCharacterName(canvas, data.Name, position);

        // Rounded border
        var actualBorderColor = borderColor ?? BorderColor;
        var border = new RoundedRectanglePolygon(new RectangleF(position, canvasSize), CornerRadius);
        canvas.Draw(Pens.Solid(actualBorderColor, BorderThickness), border);
        canvas.Restore();
    }

    public void RenderHeader(
        DrawingCanvas canvas,
        int outputWidth,
        string topString,
        string uid,
        Color? borderColor = null)
    {
        const int headerHeight = 120;
        const int headerX = 50;
        var headerWidth = outputWidth - 100;

        canvas.DrawRoundedRectangleOverlay(headerWidth, headerHeight, new PointF(headerX, 25),
            new RoundedRectangleOverlayStyle(Color.Transparent, borderColor ?? BorderColor, BorderWidth: 2, CornerRadius: 15));

        canvas.DrawText(new RichTextOptions(m_Style.Fonts.Title)
        {
            Origin = new Vector2(headerX + 20, 50),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        }, topString, Brushes.Solid(Color.White), null);

        canvas.DrawText(new RichTextOptions(m_Style.Fonts.Normal)
        {
            Origin = new Vector2(headerX + 20, 105),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        }, $"UID: {uid}", Brushes.Solid(Color.White), null);

        canvas.DrawAttribution(new RichTextOptions(m_Style.Fonts.Tiny)
        {
            Origin = new Vector2(30 + headerWidth, 15 + headerHeight),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            TextAlignment = TextAlignment.End
        }, new AttributionStyle(TextColor: Color.White));
    }

    public Image<Rgba32> RenderFooterModule(string text, int count, Color borderColor, Image? icon = null)
    {
        const int padding = 10;
        const int gapBetweenLeftAndCount = 30;
        const int iconSize = 40;
        const int height = 70;

        var textSize = TextMeasurer.MeasureBounds(text, new TextOptions(m_Style.Fonts.Normal));
        var countSize = TextMeasurer.MeasureBounds(count.ToString(), new TextOptions(m_Style.Fonts.Normal));

        var leftWidth = icon != null
            ? padding + iconSize + padding + (int)textSize.Width
            : padding + (int)textSize.Width;
        var totalWidth = leftWidth + gapBetweenLeftAndCount + (int)countSize.Width + padding;

        Image<Rgba32> module = new(totalWidth, height, Color.Transparent.ToPixel<Rgba32>());
        module.Mutate(ctx => ctx.Paint(canvas =>
        {
            var path = new RoundedRectanglePolygon(new RectangleF(1, 1, totalWidth - 2, height - 2), 10);
            canvas.Draw(Pens.Solid(borderColor, 2f), path);

            if (icon != null)
            {
                canvas.DrawImage(icon, icon.Bounds,
                    new RectangleF(padding, (height - iconSize) / 2, iconSize, iconSize),
                    KnownResamplers.Bicubic);
            }

            var textX = icon != null
                ? padding + iconSize + padding + textSize.Width / 2
                : padding + textSize.Width / 2;

            canvas.DrawText(new RichTextOptions(m_Style.Fonts.Normal)
            {
                Origin = new Vector2(textX, height / 2),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }, text, Brushes.Solid(m_Style.FooterTextColor), null);

            canvas.DrawText(new RichTextOptions(m_Style.Fonts.Normal)
            {
                Origin = new Vector2(totalWidth - padding, height / 2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            }, count.ToString(), Brushes.Solid(m_Style.FooterTextColor), null);
        }));
        return module;
    }

    public static void RenderFooter(
        DrawingCanvas canvas,
        int outputWidth,
        int gridOutputHeight,
        List<Image<Rgba32>> footerModules,
        DisposableBag disposables)
    {
        const int footerHeight = 100;
        const int footerX = 50;
        var footerWidth = outputWidth - 100;
        var footerY = gridOutputHeight - 100; // PaddingBottom(120) + offset(20)

        const int moduleH = 70;
        const int spacing = 10;
        const int footerPadding = 20;
        var totalModuleWidth = footerModules.Sum(m => m.Width) + (footerModules.Count - 1) * spacing + footerPadding * 2;
        var scale = 1f;
        if (totalModuleWidth > footerWidth)
        {
            scale = (float)footerWidth / totalModuleWidth;
        }

        var scaledSpacing = spacing * scale;
        var scaledFooterPadding = footerPadding * scale;
        var totalScaledWidth = footerModules.Sum(m => m.Width * scale) + (footerModules.Count - 1) * scaledSpacing + scaledFooterPadding * 2;
        var moduleStartX = footerX + (footerWidth - totalScaledWidth) / 2f + scaledFooterPadding;
        var moduleStartY = footerY + (footerHeight - moduleH * scale) / 2f;

        _ = canvas.SaveLayer();

        canvas.DrawRoundedRectangleOverlay(footerWidth, footerHeight, new PointF(footerX, footerY),
            new RoundedRectangleOverlayStyle(Color.Transparent, BorderColor, BorderWidth: 2, CornerRadius: 15));

        var currentX = moduleStartX;
        for (var i = 0; i < footerModules.Count; i++)
        {
            canvas.DrawImage(footerModules[i], footerModules[i].Bounds,
                new RectangleF(new Point((int)currentX, (int)moduleStartY),
                    new Size((int)(footerModules[i].Width * scale), (int)(moduleH * scale))), KnownResamplers.Bicubic);
            currentX += footerModules[i].Width * scale + scaledSpacing;
        }
        canvas.Restore();
    }

    private void DrawAvatar(DrawingCanvas canvas, CharacterModuleData data, Point position)
    {
        var width = AvatarSize.Width;
        var height = AvatarSize.Height;

        using var region = canvas.CreateRegion(new Rectangle(position, AvatarSize));
        var clipPath = new RoundedRectanglePolygon(new RectangleF(Point.Empty, AvatarSize), 10);
        _ = region.Save(ClipOptions, clipPath);

        var rectangle = new Rectangle(0, height - LevelOverlaySize.Height,
            LevelOverlaySize.Width, LevelOverlaySize.Height);
        var levelTextY = height - LevelOverlaySize.Height / 2;

        region.Fill(Brushes.Solid(m_Style.RarityColors[data.Rarity - 1]));
        region.DrawImage(data.AvatarImage, data.AvatarImage.Bounds, new RectangleF(Point.Empty, data.AvatarImage.Size), KnownResamplers.Bicubic);

        region.Fill(Brushes.Solid(m_Style.LevelOverlayColor), rectangle);

        // Constellation
        if (data.ConstellationNum > 0)
        {
            region.DrawRoundedRectangleOverlay(30, 30, new PointF(width - 35, height - 65),
                new RoundedRectangleOverlayStyle(
                    data.ConstellationNum == 6 ? m_Style.GoldConstColor : m_Style.NormalConstColor,
                    CornerRadius: 5));
            region.Restore(); // Restore before drawing text due to bug in ImageSharp.Drawing 3.0.0
            region.DrawText(new RichTextOptions(m_Style.Fonts.Normal)
            {
                Origin = new Vector2(width - 20, height - 50),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
                data.ConstellationNum.ToString()!,
                Brushes.Solid(data.ConstellationNum == 6 ? m_Style.GoldConstTextColor : Color.White), null);
            _ = region.Save(ClipOptions, clipPath);
        }

        if (data.Icon != null)
        {
            region.DrawImage(data.Icon, data.Icon.Bounds, new RectangleF(new Point(5, 5), data.Icon.Size), KnownResamplers.Bicubic);
        }

        if (m_Style.AvatarBorderColor.HasValue)
        {
            var borderPath = new RoundedRectanglePolygon(new RectangleF(Point.Empty, new Size(width - 1, height - 1)), 10);
            region.Draw(Pens.Solid(m_Style.AvatarBorderColor.Value, 5), borderPath);
        }
        region.Restore();

        var levelText = $"Lv. {data.Level}";
        var levelRect = TextMeasurer.MeasureBounds(levelText, new TextOptions(m_Style.Fonts.Small!));
        region.DrawText(new RichTextOptions(m_Style.Fonts.Small!)
        {
            Origin = new Vector2((width - levelRect.Width) / 2, levelTextY - levelRect.Height / 2),
        }, levelText, Brushes.Solid(m_Style.LevelTextColor), null);
    }

    private void DrawWeapon(DrawingCanvas canvas, WeaponModuleData weapon, Point position)
    {
        var width = WeaponSize.Width;
        var height = WeaponSize.Height;

        using var region = canvas.CreateRegion(new Rectangle(position, WeaponSize));
        var clipPath = new RoundedRectanglePolygon(new RectangleF(Point.Empty, WeaponSize), 10);
        _ = region.Save(ClipOptions, clipPath);

        var rectangle = new Rectangle(0, height - LevelOverlaySize.Height,
            LevelOverlaySize.Width, LevelOverlaySize.Height);
        var levelTextY = height - LevelOverlaySize.Height / 2;

        region.Fill(Brushes.Solid(m_Style.RarityColors[weapon.Rarity - 1]), new Rectangle(0, 0, width, height));
        region.DrawImage(weapon.WeaponImage, weapon.WeaponImage.Bounds,
            new RectangleF(Point.Empty, weapon.WeaponImage.Size), KnownResamplers.Bicubic);

        region.Fill(Brushes.Solid(m_Style.LevelOverlayColor), rectangle);

        // Affix level as const text
        if (weapon.AffixLevel > 0)
        {
            region.DrawRoundedRectangleOverlay(30, 30, new PointF(5, height - 65),
                new RoundedRectangleOverlayStyle(
                    weapon.AffixLevel == 5 ? m_Style.GoldConstColor : m_Style.NormalConstColor,
                    CornerRadius: 5));
            region.Restore(); // Restore before drawing text due to bug in ImageSharp.Drawing 3.0.0
            region.DrawText(new RichTextOptions(m_Style.Fonts.Normal)
            {
                Origin = new Vector2(20, height - 50),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
                weapon.AffixLevel.ToString()!,
                Brushes.Solid(weapon.AffixLevel == 5 ? m_Style.GoldConstTextColor : Color.White), null);
            _ = region.Save(ClipOptions, clipPath);
        }

        if (m_Style.AvatarBorderColor.HasValue)
        {
            var borderPath = new RoundedRectanglePolygon(new RectangleF(Point.Empty, new Size(width - 1, height - 1)), 10);
            region.Draw(Pens.Solid(m_Style.AvatarBorderColor.Value, 5), borderPath);
        }
        region.Restore();

        var levelText = $"Lv. {weapon.Level}";
        var levelRect = TextMeasurer.MeasureBounds(levelText, new TextOptions(m_Style.Fonts.Small!));
        region.DrawText(new RichTextOptions(m_Style.Fonts.Small!)
        {
            Origin = new Vector2((width - levelRect.Width) / 2, levelTextY - levelRect.Height / 2),
        }, levelText, Brushes.Solid(m_Style.LevelTextColor), null);
    }

    private void DrawCharacterName(DrawingCanvas canvas, string name, Point basePosition)
    {
        var nameCenter = GetNameCenter();
        var nameAreaWidth = GetNameAreaWidth();
        var fonts = new[] { m_Style.Fonts.Normal, m_Style.Fonts.Medium, m_Style.Fonts.Small, m_Style.Fonts.Tiny };
        Font? chosenFont = null;
        FontRectangle textSize = default;

        foreach (var font in fonts)
        {
            var measureOptions = new RichTextOptions(font)
            {
                Origin = Vector2.Zero,
                WrappingLength = nameAreaWidth
            };

            textSize = TextMeasurer.MeasureBounds(name, measureOptions);
            var lineCount = TextMeasurer.CountLines(name, measureOptions);
            if ((lineCount == 1 && textSize.Width <= nameAreaWidth) ||
                (lineCount > 1 && textSize.Width <= nameAreaWidth && textSize.Height <= NameAreaHeight))
            {
                chosenFont = font;
                break;
            }
        }

        chosenFont ??= fonts[^1];

        var drawOptions = new RichTextOptions(chosenFont)
        {
            Origin = new Vector2(basePosition.X + nameCenter.X,
                basePosition.Y + nameCenter.Y - textSize.Height / 2),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            TextAlignment = TextAlignment.Center,
            WrappingLength = nameAreaWidth
        };

        canvas.DrawText(drawOptions, name, Brushes.Solid(m_Style.NameColor), null);
    }

    private Point GetNameCenter() => m_Style.DrawWeapon ? NameCenter : NoWeaponNameCenter;
    private int GetNameAreaWidth() => m_Style.DrawWeapon ? NameAreaWidth : NoWeaponNameAreaWidth;
}
