#region

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Renderers.Extensions;

public record RosterLayout(
    int MaxSlots,
    int AvatarWidth = 150,
    int CanvasHeight = 200,
    int Spacing = 10);

public static class RosterImageBuilder
{
    public static Image<Rgba32> Build(
        IEnumerable<Image> avatars,
        RosterLayout layout,
        Image? trailingImage = null)
    {
        var avatarList = avatars as IReadOnlyList<Image> ?? [.. avatars];
        var totalSlots = avatarList.Count + (trailingImage != null ? 1 : 0);
        var canvasWidth = layout.MaxSlots * layout.AvatarWidth + (layout.MaxSlots - 1) * layout.Spacing + 20;

        var offset = (layout.MaxSlots - totalSlots) * layout.AvatarWidth / 2 + 10;

        Image<Rgba32> rosterImage = new(canvasWidth, layout.CanvasHeight);

        rosterImage.Mutate(ctx =>
        {
            ctx.Clear(Color.Transparent);

            for (var i = 0; i < avatarList.Count; i++)
            {
                var x = offset + i * (layout.AvatarWidth + layout.Spacing);
                ctx.DrawImage(avatarList[i], new Point(x, 0), 1f);
            }

            if (trailingImage != null)
            {
                var x = offset + avatarList.Count * (layout.AvatarWidth + layout.Spacing);
                ctx.DrawImage(trailingImage, new Point(x, 0), 1f);
            }
        });

        return rosterImage;
    }
}
