#region

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Shared.Renderers.Extensions;

public record RosterLayout(
    int MaxSlots,
    int AvatarWidth = 150,
    int CanvasHeight = 200,
    int Spacing = 10);

public static class RosterImageBuilder
{
    public static void Draw<TItem>(
        IEnumerable<TItem> items,
        RosterLayout layout,
        Point canvasOrigin,
        Action<Point, TItem> drawItem)
    {
        var itemList = items as IReadOnlyList<TItem> ?? [.. items];
        var offset = (layout.MaxSlots - itemList.Count) * layout.AvatarWidth / 2 + 10;

        for (var i = 0; i < itemList.Count; i++)
        {
            var x = offset + i * (layout.AvatarWidth + layout.Spacing);
            drawItem(new Point(canvasOrigin.X + x, canvasOrigin.Y), itemList[i]);
        }
    }
}
