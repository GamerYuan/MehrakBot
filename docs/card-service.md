# Card Service Guide

This document explains how card renderers work in `Mehrak.Application`.

All paths below are relative to the `MehrakBot/` solution directory (the directory where the `dotnet` commands run):

- `Common/Mehrak.Domain/Card/ICardService.cs`
- `Common/Mehrak.Domain/User/Abstractions/ICardGenerationContext.cs`
- `Common/Mehrak.Domain/Shared/Services/IAsyncInitializable.cs`
- `Common/Mehrak.Domain/ServiceCollectionExtensions.cs`
- `Services/Application/Mehrak.Application/Shared/Renderers/CardServiceBase.cs`
- `Services/Application/Mehrak.Application/Shared/Renderers/CharacterCardServiceBase.cs`
- `Services/Application/Mehrak.Application/Shared/Renderers/AttributionRenderer.cs`
- `Services/Application/Mehrak.Application/Shared/Renderers/CharacterModuleRenderer.cs`
- `Services/Application/Mehrak.Application/Shared/Services/BaseApplicationService.cs`
- `Services/Application/Mehrak.Application/Shared/Services/BaseCardGenerationContext.cs`
- `Services/Application/Mehrak.Application/Shared/Services/AsyncInitializationHostedService.cs`
- `Services/Application/Mehrak.Application/Genshin/GenshinApplicationServiceExtensions.cs`

Reference implementations:

- `Services/Application/Mehrak.Application/Genshin/Character/GenshinCharacterCardService.cs`
- `Services/Application/Mehrak.Application/Genshin/CharList/GenshinCharListCardService.cs`

## What A Card Service Is

A card service renders a typed card and implements:

```csharp
public interface ICardService<TData>
{
    Task<Stream> GetCardAsync(
        ICardGenerationContext<TData> context,
        CancellationToken cancellationToken = default);
}
```

The context exposes:

- `UserId`
- `Data` (the typed model used for rendering)
- `GameProfile` (nickname, UID, level, etc.)
- `PortraitImageStream` and `PortraitConfig` for character cards
- `GetParameter<TParam>(key)` / `SetParameter(key, value)` for command-specific inputs

The returned stream is JPEG data created by the base service. **The caller owns the returned stream and must dispose it** (application services use `using` or `await using`). The base disposes request-scoped images after `GetCardAsync` completes, but it does not dispose the stream it returns.

## The Shared Base Class

All current card services derive from `CardServiceBase<TData>`; character card services derive from `CharacterCardServiceBase<TData>`, which adds the shared portrait-loading logic.

`CardServiceBase<TData>` implements `ICardService<TData>` and `IAsyncInitializable`, and owns the request lifecycle:

- starts the card-generation timer (`IApplicationMetrics.ObserveCardGenerationDuration`) and writes start/success/error logs
- creates the background through `CreateBackground(context)`
- calls the renderer's `RenderCardAsync` with a `DisposableBag`
- encodes the result as JPEG and rewinds the returned stream to position `0`
- disposes request-scoped resources in `finally`
- applies the shared cancellation/error policy described below

Renderers implement two abstract members:

```csharp
public abstract Task LoadStaticResourcesAsync(CancellationToken cancellationToken = default);

public abstract Task RenderCardAsync(
    Image<Rgba32> background,
    ICardGenerationContext<TData> context,
    DisposableBag disposables,
    CancellationToken cancellationToken = default);
```

`InitializeAsync` is supplied by the base class. It calls `LoadStaticResourcesAsync`; renderers should override `LoadStaticResourcesAsync`, not `InitializeAsync`.

The default `CreateBackground` behavior is:

- if `StaticBackground` is set, return a request-local `CloneAs<Rgba32>()`
- otherwise, return a new `1920x1080` image

The default `CreateBackground(context)` delegates to `CreateBackground()`. Override `CreateBackground(context)` when the output size depends on data, or `CreateBackground()` when a fixed custom size is needed. Always return an image that the caller can mutate without changing a shared instance; `StaticBackground` itself must stay immutable during rendering.

## Static Resources And Initialization

`LoadStaticResourcesAsync` is invoked once by `AsyncInitializationHostedService` at application startup for registered card services. It is the place for reusable assets that should not be loaded per request.

Two representative Genshin card renderers use this path:

- `GenshinCharacterCardService` preloads stat icons, star-rating images, relic templates, and its overlay.
- `GenshinCharListCardService` preloads its element icons and small element icons.

Both listed renderers are registered for startup initialization, and the other Genshin card services also register their initializers; these are not on-demand-only renderers. Use `LoadStaticResourcesAsync` for reusable assets rather than reloading them on every `GetCardAsync` call.

Resources loaded once belong to the singleton card service. Do not add them to a request `DisposableBag`. If the service implements `IDisposable`, dispose long-lived images from `Dispose`; `GenshinArcanaCardService` is the example. A process-lifetime singleton may intentionally retain its preloaded images for the lifetime of the process.

## Resource Management

`DisposableBag` is a small concurrent bag used for per-request resources. The base creates it, adds the background image, disposes it in `finally`, and tolerates exceptions from individual `Dispose` calls.

Renderer rules:

- Add every image loaded for the current request to `disposables`; the base helpers `LoadImageFromRepositoryAsync` and `LoadImageFromStreamAsync` already do this.
- Do not dispose request-scoped images yourself; the base disposes the bag after the stream has been produced.
- Do not add shared/preloaded resources to the bag.
- Do not mutate a shared `StaticBackground`; draw on the per-request clone.
- The stream supplied through `context.PortraitImageStream` is owned by the caller; load it into an image but do not dispose the stream.
- Reset seekable input streams before loading if you use a stream directly. The base stream-loading helper does this.

## Character Portraits

`CharacterCardServiceBase<TData>` centralizes portrait selection:

- if `context.PortraitImageStream` is set, it is loaded as the user portrait
- otherwise the renderer's stock-image loader is used
- `PortraitConfig.TargetScale` is applied with bounded resizing
- `PortraitConfig.FlipX` is applied to the portrait
- layout-specific `OffsetX` / `OffsetY` values remain the renderer's responsibility

Application services resolve portraits and assign `PortraitImageStream` and `PortraitConfig` on `BaseCardGenerationContext<TData>`, pass command inputs through `SetParameter`, and dispose any portrait stream they created. The renderer must not dispose that supplied stream.

## Shared Attribution

Cards draw the shared MehrakBot attribution through `AttributionRenderer`; do not duplicate the attribution text in a renderer.

The recommended default placement is the bottom-right safe area:

```csharp
canvas.DrawAttribution(new RichTextOptions(Fonts.Tiny)
{
    Origin = new PointF(background.Width - 20, background.Height - 20),
    HorizontalAlignment = HorizontalAlignment.Right,
    VerticalAlignment = VerticalAlignment.Bottom,
    TextAlignment = TextAlignment.End
});
```

`DrawAttribution` uses `AttributionRenderer.Default` when no style is supplied: white text with a drop shadow. `TextAlignment.End` keeps the two attribution lines aligned to the right edge.

Placement is layout-specific, not a global rule. Character cards that draw over layered content use `DrawBoundedAttribution` (an internal helper for renderers inside `Mehrak.Application`) so the shadow layer is bounded to the measured attribution text and shadow bounds rather than the whole canvas; other character cards use `DrawAttribution` at a layout-specific anchor near the portrait information area. Character lists draw attribution through `CharacterModuleRenderer.RenderHeader`; do not add a second mark there. Use the shared renderer, whichever placement fits the layout.

If a portrait supplies an artist credit, it comes only from `context.PortraitConfig?.ArtistAttribution`. Pass it through `extraText` only when it is non-empty; do not invent or hard-code credits. For example:

```csharp
var artist = context.PortraitConfig?.ArtistAttribution;
canvas.DrawAttribution(options,
    extraText: string.IsNullOrWhiteSpace(artist) ? null : $"Cre: {artist}");
```

`extraText` is prepended before the standard MehrakBot lines.

## Errors And Cancellation

Do not add a second try/catch/log pipeline inside `RenderCardAsync`. The base `GetCardAsync` already:

- propagates `ImageNotFoundException` unchanged so `BaseAttachmentApplicationService` can retry the image update flow
- propagates `OperationCanceledException` unchanged
- rethrows an existing `CommandException`
- logs and wraps any other exception in a `CommandException("Failed to generate ... card", ex)`
- disposes the output stream on every failure path

Pass the cancellation token to downloads, image loads, and other asynchronous work. Throw `CommandException` directly only for renderer validation failures that callers need to surface as command failures.

## Registration

Register card services as singletons and register their startup initialization:

```csharp
services.AddSingleton<ICardService<ExampleData>, ExampleCardService>();
services.RegisterAsyncInitializableFor<ICardService<ExampleData>, ExampleCardService>();
```

`RegisterAsyncInitializableFor` creates a scoped `IAsyncInitializable` alias that resolves the singleton service. `AsyncInitializationHostedService` creates one scope at startup and awaits every initializer.

A card service that implements `IDisposable` needs the explicit singleton alias instead, because a scoped alias would dispose the singleton when the startup scope ends:

```csharp
services.AddSingleton<ICardService<GenshinTarotCardState>, GenshinArcanaCardService>();
services.AddSingleton<IAsyncInitializable>(sp =>
    (GenshinArcanaCardService)sp.GetRequiredService<ICardService<GenshinTarotCardState>>());
```

`GenshinArcanaCardService` and `GenshinTheaterCardService` currently use this pattern. Keyed card services use `AddKeyedSingleton` plus `RegisterAsyncInitializableForKeyed`; that helper also creates a scoped alias, so a keyed renderer that implements `IDisposable` needs its `IAsyncInitializable` alias registered through a singleton factory for the same reason. No current keyed card service is disposable.

## Cache Fingerprints And Renderer Versions

Card caching is the application service's responsibility, not the renderer's.

`BaseAttachmentApplicationService.GetCardFileName(...)` builds a `CardRenderFingerprint` containing:

- schema version
- game and mode
- renderer version
- game UID, nickname, and level
- typed card data
- effective command inputs

The fingerprint is SHA-256 hashed into a deterministic `.jpg` filename. Card commands should use `GetCardFileName`, which builds that typed fingerprint; `GetFileName` is the lower-level hash helper. The application service checks `AttachmentExistsAsync` before rendering and returns the stored attachment when the fingerprint matches.

A renderer version is part of that fingerprint. Bump it whenever a renderer change alters the produced pixels; data or input changes invalidate the cache automatically, but renderer code changes do not. The Arcana command's current version is `v9`; adding its attribution required invalidating images cached under `v8`.

## Minimal Base-Derived Example

This example uses the base lifecycle directly. It loads one request-scoped image, draws onto the background, and lets the base handle cloning, encoding, timing, cleanup, and errors.

```csharp
internal sealed class ExampleCardService : CardServiceBase<ExampleData>
{
    public ExampleCardService(
        IImageRepository imageRepository,
        ILogger<ExampleCardService> logger,
        IApplicationMetrics metrics)
        : base(
            "Example",
            imageRepository,
            logger,
            metrics,
            LoadFonts("Assets/Fonts/genshin.ttf", titleSize: 56, normalSize: 32, smallSize: 24, tinySize: 20))
    {
    }

    public override Task LoadStaticResourcesAsync(CancellationToken cancellationToken = default)
    {
        // Load long-lived assets here and keep them on the singleton.
        return Task.CompletedTask;
    }

    public override async Task RenderCardAsync(
        Image<Rgba32> background,
        ICardGenerationContext<ExampleData> context,
        DisposableBag disposables,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var icon = await LoadImageFromRepositoryAsync("example/icon.png", disposables, cancellationToken);

        background.Mutate(ctx =>
        {
            ctx.DrawImage(icon, new Point(60, 60), 1f);

            ctx.Paint(canvas =>
            {
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new PointF(60, 220)
                }, context.Data.Title, Brushes.Solid(Color.White), null);

                canvas.DrawAttribution(new RichTextOptions(Fonts.Tiny)
                {
                    Origin = new PointF(background.Width - 20, background.Height - 20),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    TextAlignment = TextAlignment.End
                });
            });
        });
    }
}
```

Register it as shown above, and let the calling application service build the context, compute the cache filename, and store the returned stream.

## Performance Notes

- Preload reusable assets once in `LoadStaticResourcesAsync`.
- Deduplicate per-request loads when multiple card entries share an asset.
- Use `Task.WhenAll` for independent downloads and image loads.
- Avoid resizing shared assets during rendering; resize per-request images only.
- Do not add a manual metrics timer or encoder; the base already provides the duration metric and reusable JPEG encoder.

## Contributor Checklist

When adding or changing a card renderer:

1. Derive from `CardServiceBase<TData>` (or `CharacterCardServiceBase<TData>` for character cards).
2. Implement `LoadStaticResourcesAsync` and `RenderCardAsync` with the correct cancellation token.
3. Add every request-scoped image to the `DisposableBag`; keep long-lived assets out of it.
4. Draw the shared attribution in a layout-appropriate position; use `TextAlignment.End` for right-aligned multi-line text, and only use `PortraitConfig.ArtistAttribution` when supplied.
5. Register the card service as a singleton and register its async initialization, using the `IDisposable` singleton variant when needed.
6. Rely on the base service for timing, logging, JPEG encoding, cleanup, cancellation, and exception wrapping.
7. Bump the application service's renderer version whenever rendered pixels change.
8. Verify output dimensions, readable labels, attribution visibility, and cache invalidation with the focused tests or a generated preview.
