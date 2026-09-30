using System.Globalization;
using System.Numerics;
using Mehrak.Application.Renderers.Extensions;
using Mehrak.Application.Shared.Renderers;
using Mehrak.Application.Shared.Renderers.Extensions;
using Mehrak.Application.Tests.TestUtils;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Mehrak.Application.Tests.Shared.Renderers;

public sealed class BoundedLayerRenderingTests
{
    private static readonly FontCollection FontCollection = new();
    private static readonly FontFamily FontFamily = FontCollection.Add(
        System.IO.Path.Combine(AppContext.BaseDirectory, "Assets/Fonts/zzz.ttf"));
    private static readonly Font Font = FontFamily.CreateFont(30);
    private static readonly Font SmallFont = FontFamily.CreateFont(20);

    [Test]
    public void BoundedShadow_FontOverload_MatchesDefaultOutput()
    {
        AssertShadowEquivalent("Mehrak", new PointF(32.5f, 28.25f), new DropShadowTextStyle(), null);
    }

    [Test]
    public void BoundedShadow_RichMultilingualWrapping_MatchesDefaultOutput()
    {
        const string text = "属性异常 / Anomaly\nÉnergie détaillée";
        var options = new RichTextOptions(Font)
        {
            Origin = new PointF(198.75f, 31.25f),
            WrappingLength = 155,
            HorizontalAlignment = HorizontalAlignment.Right,
            TextAlignment = TextAlignment.End,
            TextRuns =
            [
                new RichTextRun { Start = 0, End = 4, Font = FontFamily.CreateFont(35) },
                new RichTextRun { Start = 13, End = text.Length, Font = SmallFont }
            ]
        };
        AssertShadowEquivalent(text, options, new DropShadowTextStyle(ShadowOffsetX: -7.5f, ShadowOffsetY: 6.25f),
            Matrix3x2.CreateTranslation(-16.5f, 9.25f));
    }

    [Test]
    public void BoundedShadow_NegativeOffsets_MatchesDefaultOutput()
    {
        AssertShadowEquivalent("Negative", new PointF(14.25f, 44.5f),
            new DropShadowTextStyle(Color.FromPixel(new Rgba32(5, 5, 5, 190)), -12.5f, -8.25f), null);
    }

    [Test]
    public void BoundedShadow_OutlinedRun_MatchesDefaultOutput()
    {
        const string text = "Outlined 属性";
        var options = new RichTextOptions(Font)
        {
            Origin = new PointF(28, 36),
            TextRuns = [new RichTextRun { Start = 0, End = text.Length, Pen = Pens.Solid(Color.DarkOrange, 8) }]
        };
        AssertShadowEquivalent(text, options, new DropShadowTextStyle(ShadowOffsetX: 6, ShadowOffsetY: -5), null);
    }

    [Test]
    public void BoundedShadow_MultilineOverflow_MatchesDefaultOutput()
    {
        var options = new RichTextOptions(Font)
        {
            Origin = new PointF(112, 18),
            HorizontalAlignment = HorizontalAlignment.Center,
            WrappingLength = 80,
            TextAlignment = TextAlignment.Center
        };
        AssertShadowEquivalent("OVERFLOWING\n多言語\nTEXT", options,
            new DropShadowTextStyle(ShadowOffsetX: 4, ShadowOffsetY: 7), null);
    }

    [Test]
    public void BoundedShadow_EmptyText_MatchesDefaultAndBalancesState()
    {
        using var expected = Render(90, 80, canvas =>
            canvas.DrawTextWithShadow(string.Empty, Font, new PointF(12.5f, 19.5f), Color.White));
        using var actual = Render(90, 80, canvas =>
        {
            var saveCount = canvas.SaveCount;
            canvas.DrawBoundedTextWithShadow(string.Empty, Font, new PointF(12.5f, 19.5f), Color.White);
            Assert.That(canvas.SaveCount, Is.EqualTo(saveCount));
        });
        AssertEquivalentWithSameVisibleExtents(expected, actual);
    }

    [Test]
    public void BoundedIcon_NegativePaddingAndOffEdgeContent_MatchesDefaultOutput()
    {
        using var icon = CreateIcon(18, 12);
        AssertIconEquivalent(icon, new PointF(9.75f, 63.5f), 31.5f, -14f, 7f, null);
    }

    [Test]
    public void BoundedIcon_TransparentFillAndWideOutline_MatchesDefaultOutput()
    {
        using var icon = CreateIcon(32, 26);
        AssertIconEquivalent(icon, new PointF(83.25f, 62.75f), 37.5f, 6f, 11f, null);
    }

    [Test]
    public void BoundedIcon_Transform_MatchesDefaultOutput()
    {
        using var icon = CreateIcon(30, 38);
        AssertIconEquivalent(icon, new PointF(78, 55), 32, 4, 5,
            Matrix3x2.CreateScale(0.85f, 1.1f, new Vector2(78, 55)) * Matrix3x2.CreateTranslation(7, 3));
    }

    [Test]
    public void BoundedIcon_ChildRegionUsesLocalBounds_MatchesDefaultOutput()
    {
        using var icon = CreateIcon(25, 25);
        using var expected = Render(210, 150, canvas =>
        {
            using var region = canvas.CreateRegion(new Rectangle(45, 22, 130, 105));
            region.DrawCenteredIcon(icon, new PointF(34, 42), 29, -3, Color.Transparent, Color.Gold, 6);
        });
        using var actual = Render(210, 150, canvas =>
        {
            using var region = canvas.CreateRegion(new Rectangle(45, 22, 130, 105));
            region.DrawBoundedCenteredIcon(icon, new PointF(34, 42), 29, -3, Color.Transparent, Color.Gold, 6);
        });
        AssertEquivalentWithSameVisibleExtents(expected, actual);
    }

    [Test]
    public void BoundedEllipse_OverflowingMultilingualText_MatchesDefaultOutput()
    {
        AssertEllipseEquivalent("MULTILINGUAL 属性 12345", new PointF(115.4f, 64.6f), 27.25f,
            new EllipseTextStyle(Font, Color.White, Color.FromPixel(new Rgba32(35, 50, 80, 180)),
                Color.Gold, 9f, ShrinkToFit: false), null);
    }

    [Test]
    public void BoundedEllipse_ShrinkToFit_MatchesDefaultOutput()
    {
        AssertEllipseEquivalent("Long text", new PointF(90, 65), 38,
            new EllipseTextStyle(Font, Color.White, Color.DarkSlateBlue, ShrinkToFit: true), null);
    }

    [Test]
    public void BoundedEllipse_TransparentOutlinedTransform_MatchesDefaultOutput()
    {
        AssertEllipseEquivalent("R5", new PointF(90, 65), 31,
            new EllipseTextStyle(Font, Color.White, Color.Transparent, Color.Cyan, 8, false),
            Matrix3x2.CreateRotation(0.12f, new Vector2(90, 65)));
    }

    [Test]
    public void BoundedStat_WrappedNameAndBreakdown_MatchesDefaultOutput()
    {
        using var icon = CreateIcon(24, 24);
        AssertStatEquivalent(new StatLineData("非常に長い属性名 with wrapped details", "12,345.67%", "10,000.12", "+2,345.55"),
            new StatLineStyle(icon, Font, Color.White, SmallFont, Color.LightGray, Color.LightGreen),
            new PointF(-7.6f, 46.4f), 445.75f, null);
    }

    [Test]
    public void BoundedStat_NoIcon_MatchesDefaultOutput()
    {
        AssertStatEquivalent(new StatLineData("Critical Rate", "88.8%", "50.0%", "+38.8%"),
            new StatLineStyle(null, Font, Color.White, SmallFont), new PointF(18, 38), 390, null);
    }

    [Test]
    public void BoundedStat_NoBreakdownWithTransform_MatchesDefaultOutput()
    {
        using var icon = CreateIcon(35, 29);
        AssertStatEquivalent(new StatLineData("攻撃力", "3,456"),
            new StatLineStyle(icon, Font, Color.White), new PointF(20, 42), 370,
            Matrix3x2.CreateTranslation(12.5f, -7.25f));
    }

    [Test]
    public void BoundedAttribution_DefaultShadowAndExtraText_MatchesDefaultOutput()
    {
        var options = AttributionOptions(new PointF(300, 145));
        AssertAttributionEquivalent(options, null, "Cre: 多言語 Artist", null);
    }

    [Test]
    public void BoundedAttribution_OpacityOutlineAndRotation_MatchesDefaultOutput()
    {
        var options = AttributionOptions(new PointF(275, 130));
        var style = new AttributionStyle(Color.White, Color.Black, 7, 0.55f, -11f);
        AssertAttributionEquivalent(options, style, "Rotated", null);
    }

    [Test]
    public void BoundedAttribution_UnsupportedDecorationFallsBack_MatchesDefaultOutput()
    {
        var options = AttributionOptions(new PointF(290, 140));
        options.TextRuns =
        [
            new RichTextRun { Start = 0, End = 9, UnderlinePen = Pens.Solid(Color.Red, 3) }
        ];
        var style = AttributionRenderer.Default with { Opacity = 0.7f };
        AssertAttributionEquivalent(options, style, null, null);
    }

    [Test]
    public void BoundedRankLayer_TransparentIconAndXorText_MatchesUnboundedReference()
    {
        using var icon = new Image<Rgba32>(96, 58, new Rgba32(45, 120, 210, 145));
        AssertRankEquivalent(icon, new RectangleF(41.25f, 29.5f, icon.Width, icon.Height),
            $"{(12345.67f).ToString("N2", CultureInfo.GetCultureInfo("de-DE"))}%",
            new PointF(52.5f, 49.25f), null, null);
    }

    [Test]
    public void BoundedRankLayer_RegionAndTransform_MatchesUnboundedReference()
    {
        using var icon = new Image<Rgba32>(78, 52, new Rgba32(180, 60, 220, 125));
        AssertRankEquivalent(icon, new RectangleF(8, 10, 78, 52), "99.99%", new PointF(17, 25),
            new Rectangle(55, 28, 120, 95), Matrix3x2.CreateTranslation(4.5f, 3.25f));
    }

    [Test]
    public void InvalidBounds_FallsBackToUnboundedLayerAndBalancesState()
    {
        using var expected = Render(120, 90, canvas =>
        {
            _ = canvas.SaveLayer();
            canvas.Fill(Brushes.Solid(Color.CornflowerBlue), new Rectangle(12, 18, 55, 31));
            canvas.Restore();
        });
        using var actual = Render(120, 90, canvas =>
        {
            var saveCount = canvas.SaveCount;
            _ = LayerBoundsUtility.SaveLayer(canvas, new RectangleF(float.NaN, 0, 20, 20));
            canvas.Fill(Brushes.Solid(Color.CornflowerBlue), new Rectangle(12, 18, 55, 31));
            canvas.Restore();
            Assert.That(canvas.SaveCount, Is.EqualTo(saveCount));
        });
        AssertEquivalentWithSameVisibleExtents(expected, actual);
    }

    private static void AssertShadowEquivalent(string text, PointF origin, DropShadowTextStyle style,
        Matrix3x2? transform)
    {
        using var expected = Render(280, 155, canvas => WithTransform(canvas, transform,
            target => target.DrawTextWithShadow(text, Font, origin, Color.Cornsilk, style)));
        using var actual = Render(280, 155, canvas => WithTransform(canvas, transform,
            target => target.DrawBoundedTextWithShadow(text, Font, origin, Color.Cornsilk, style)));
        AssertEquivalentWithSameVisibleExtents(expected, actual);
    }

    private static void AssertShadowEquivalent(string text, RichTextOptions options, DropShadowTextStyle style,
        Matrix3x2? transform)
    {
        using var expected = Render(300, 175, canvas => WithTransform(canvas, transform,
            target => target.DrawTextWithShadow(text, options, Color.Cornsilk, style)));
        using var actual = Render(300, 175, canvas => WithTransform(canvas, transform,
            target => target.DrawBoundedTextWithShadow(text, options, Color.Cornsilk, style)));
        AssertEquivalentWithSameVisibleExtents(expected, actual);
    }

    private static void AssertIconEquivalent(Image icon, PointF center, float radius, float padding,
        float outlineWidth, Matrix3x2? transform)
    {
        using var expected = Render(200, 145, canvas => WithTransform(canvas, transform,
            target => target.DrawCenteredIcon(icon, center, radius, padding,
                Color.FromPixel(new Rgba32(20, 40, 90, 170)), Color.White, outlineWidth)));
        using var actual = Render(200, 145, canvas => WithTransform(canvas, transform,
            target => target.DrawBoundedCenteredIcon(icon, center, radius, padding,
                Color.FromPixel(new Rgba32(20, 40, 90, 170)), Color.White, outlineWidth)));
        AssertEquivalentWithSameVisibleExtents(expected, actual);
    }

    private static void AssertEllipseEquivalent(string text, PointF center, float radius, EllipseTextStyle style,
        Matrix3x2? transform)
    {
        using var expected = Render(330, 150, canvas => WithTransform(canvas, transform,
            target => target.DrawCenteredTextInEllipse(text, center, radius, style)));
        using var actual = Render(330, 150, canvas => WithTransform(canvas, transform,
            target => target.DrawBoundedCenteredTextInEllipse(text, center, radius, style)));
        AssertEquivalentWithSameVisibleExtents(expected, actual);
    }

    private static void AssertStatEquivalent(StatLineData data, StatLineStyle style, PointF position, float width,
        Matrix3x2? transform)
    {
        using var expected = Render(540, 180, canvas => WithTransform(canvas, transform,
            target => target.DrawStatLine(data, style, position, width)));
        using var actual = Render(540, 180, canvas => WithTransform(canvas, transform,
            target => target.DrawBoundedStatLine(data, style, position, width)));
        AssertEquivalentWithSameVisibleExtents(expected, actual);
    }

    private static void AssertAttributionEquivalent(RichTextOptions options, AttributionStyle? style,
        string? extraText, Matrix3x2? transform)
    {
        using var expected = Render(340, 180, canvas => WithTransform(canvas, transform,
            target => target.DrawAttribution(options, style, extraText)));
        using var actual = Render(340, 180, canvas => WithTransform(canvas, transform,
            target => target.DrawBoundedAttribution(options, style, extraText)));
        AssertEquivalentWithSameVisibleExtents(expected, actual);
    }

    private static void AssertRankEquivalent(Image icon, RectangleF destination, string rankText, PointF textOrigin,
        Rectangle? regionBounds, Matrix3x2? transform)
    {
        using var expected = Render(230, 150, canvas => DrawInOptionalRegion(canvas, regionBounds,
            target => WithTransform(target, transform,
                transformed => DrawRank(transformed, icon, destination, rankText, textOrigin, false))));
        using var actual = Render(230, 150, canvas => DrawInOptionalRegion(canvas, regionBounds,
            target => WithTransform(target, transform,
                transformed => DrawRank(transformed, icon, destination, rankText, textOrigin, true))));
        AssertEquivalentWithSameVisibleExtents(expected, actual);
    }

    private static void DrawRank(DrawingCanvas canvas, Image icon, RectangleF destination, string rankText,
        PointF textOrigin, bool bounded)
    {
        var textOptions = new RichTextOptions(SmallFont) { Origin = textOrigin };
        if (bounded)
        {
            _ = LayerBoundsUtility.SaveLayer(canvas,
                LayerBoundsUtility.Inflate(destination, LayerBoundsUtility.AntialiasingFringe),
                LayerBoundsUtility.GetTextBounds(rankText, textOptions));
        }
        else
        {
            _ = canvas.SaveLayer();
        }

        canvas.DrawImage(icon, icon.Bounds, destination, KnownResamplers.Bicubic);
        _ = canvas.Save(new DrawingOptions
        {
            GraphicsOptions = new GraphicsOptions { AlphaCompositionMode = PixelAlphaCompositionMode.Xor }
        });
        canvas.DrawText(textOptions, rankText, Brushes.Solid(Color.White), null);
        canvas.Restore();
        canvas.Restore();
    }

    private static void DrawInOptionalRegion(DrawingCanvas canvas, Rectangle? bounds, CanvasAction draw)
    {
        if (bounds == null)
        {
            draw(canvas);
            return;
        }

        using var region = canvas.CreateRegion(bounds.Value);
        draw(region);
    }

    private static void WithTransform(DrawingCanvas canvas, Matrix3x2? transform, CanvasAction draw)
    {
        if (transform == null)
        {
            draw(canvas);
            return;
        }

        _ = canvas.Save(new DrawingOptions { Transform = new(transform.Value) });
        draw(canvas);
        canvas.Restore();
    }

    private static RichTextOptions AttributionOptions(PointF origin) => new(SmallFont)
    {
        Origin = origin,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom,
        TextAlignment = TextAlignment.End
    };

    private static Image<Rgba32> CreateIcon(int width, int height)
        => new(width, height, new Rgba32(240, 80, 40, 170));

    private static Image<Rgba32> Render(int width, int height, CanvasAction draw)
    {
        var image = new Image<Rgba32>(width, height, Color.Transparent.ToPixel<Rgba32>());
        image.Mutate(context => context.Paint(draw));
        return image;
    }

    private static void AssertEquivalentWithSameVisibleExtents(Image<Rgba32> expected, Image<Rgba32> actual)
    {
        Assert.Multiple(() =>
        {
            Assert.That(ToPng(actual), IsImage.PerceptuallyEquivalentTo(ToPng(expected)));
            Assert.That(GetVisibleBounds(actual), Is.EqualTo(GetVisibleBounds(expected)));
        });
    }

    private static byte[] ToPng(Image<Rgba32> image)
    {
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static Rectangle GetVisibleBounds(Image<Rgba32> image)
    {
        var left = image.Width;
        var top = image.Height;
        var right = -1;
        var bottom = -1;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x].A == 0)
                        continue;
                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
            }
        });
        return right < left ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }
}
