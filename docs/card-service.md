# Card Service Guide

This document explains how card renderers are implemented in `Mehrak.Application`.

Reference implementations:

- `Mehrak.Application/Services/Genshin/Character/GenshinCharacterCardService.cs`
- `Mehrak.Application/Services/Genshin/CharList/GenshinCharListCardService.cs`

## What A Card Service Is

A card service is a renderer that implements:

```csharp
public interface ICardService<TData>
{
	Task<Stream> GetCardAsync(ICardGenerationContext<TData> context);
}
```

Sources:

- `Mehrak.Domain/Services/Abstractions/ICardService.cs`
- `Mehrak.Domain/Models/Abstractions/ICardGenerationContext.cs`

Input context includes:

- `UserId`
- `Data` (typed model used for rendering)
- `GameProfile` (nickname/uid/level etc.)
- Optional typed parameters via `GetParameter<T>(key)`

Output is a `Stream` (usually JPEG) returned to the application service for storage and response composition.

## Card Service Lifecycle

1. Application service prepares domain data and `BaseCardGenerationContext<T>`.
2. Application service calls `m_CardService.GetCardAsync(context)`.
3. Card service loads assets, composes image, and returns stream.
4. Application service stores stream and returns `CommandAttachment`.

## Two Common Patterns

### Pattern A: On-demand renderer (no async preloading)

Reference: `GenshinCharListCardService`

Characteristics:

- Implements only `ICardService<TData>`
- Loads all needed assets during `GetCardAsync(...)`
- Best for simpler rendering where startup preloading is not required

Flow example in `GenshinCharListCardService`:

1. Collect source data and sort/group for layout.
2. Load weapon images (with ascended fallback logic), cache in dictionary per unique key.
3. Build styled avatar tiles (`GetStyledCharacterImage(...)`).
4. Compute grid layout and create output canvas.
5. Draw profile header, tiles, summary chips (element and rarity counts).
6. Encode JPEG into `MemoryStream` and return.

### Pattern B: Renderer with async initialization

Reference: `GenshinCharacterCardService`

Characteristics:

- Implements `ICardService<TData>, IAsyncInitializable`
- Preloads reusable assets in `InitializeAsync(...)` (for example stat icons)
- Uses preloaded resources during `GetCardAsync(...)` for better runtime performance

Flow example in `GenshinCharacterCardService`:

1. `InitializeAsync(...)` loads and resizes stat icons into `m_StatImages`.
2. `GetCardAsync(...)` loads dynamic assets (portrait, weapon, skills, constellations, relics).
3. Applies business display rules (active set detection, constellation skill effects, stat selection).
4. Composes final `3240x1080` frame and encodes JPEG.

## Async Initialization Wiring

If your card service implements `IAsyncInitializable`, register it for startup initialization.

Relevant pieces:

- `Mehrak.Domain/Services/Abstractions/IAsyncInitializable.cs`
- `Mehrak.Domain/ServiceCollectionExtensions.cs` (`RegisterAsyncInitializableFor`)
- `Mehrak.Application/Services/Common/AsyncInitializationHostedService.cs`

Registration pattern:

```csharp
services.AddSingleton<ICardService<GenshinCharacterInformation>, GenshinCharacterCardService>();
services.RegisterAsyncInitializableFor<ICardService<GenshinCharacterInformation>, GenshinCharacterCardService>();
```

If initialization is unnecessary (like current `GenshinCharListCardService`), only singleton registration is needed.

## Resource Management Rules

Image rendering is allocation-heavy. Follow these rules:

- Track disposable `Image` objects and streams, then dispose in `finally`.
- Avoid disposing objects still needed by the output stream.
- Use short-lived clones when applying destructive effects.
- Keep shared preloaded resources immutable during render path where possible.

Both reference services keep a disposable list and release resources at the end.

## Error Handling Conventions

Inside `GetCardAsync(...)`:

- Wrap render pipeline in `try/catch`.
- Log with user and payload context.
- Throw `CommandException` for application-layer handling.

Example convention from references:

- Log `LogMessage.CardGenStartInfo` / `LogMessage.CardGenSuccess` / `LogMessage.CardGenError`.
- On failure: `throw new CommandException("Failed to generate ... card", ex);`

## Performance Notes

- Pre-resize frequently reused assets once (startup, first load or resize assets in application service).
- Deduplicate per-request asset loads (`DistinctBy` weapon key in charlist).
- Use `Task.WhenAll` for independent image load tasks.
- Keep encoder config reusable (`JpegEncoder` field/static).
- Measure performance metrics via `IApplicationMetrics.ObserveCardGenerationDuration(...)`.

## Minimal Template

```csharp
internal class ExampleCardService : ICardService<ExampleData>
{
	private readonly IImageRepository m_ImageRepository;
	private readonly ILogger<ExampleCardService> m_Logger;

	public ExampleCardService(IImageRepository imageRepository, ILogger<ExampleCardService> logger)
	{
		m_ImageRepository = imageRepository;
		m_Logger = logger;
	}

	public async Task<Stream> GetCardAsync(ICardGenerationContext<ExampleData> context)
	{
		var disposables = new List<IDisposable>();
		try
		{
			Image<Rgba32> canvas = new(1920, 1080);
			disposables.Add(canvas);

			// Load assets, compose image, draw text/components.

			MemoryStream stream = new();
			await canvas.SaveAsJpegAsync(stream);
			stream.Position = 0;
			return stream;
		}
		catch (Exception ex)
		{
			m_Logger.LogError(ex, "Failed to generate card for user {UserId}", context.UserId);
			throw new CommandException("Failed to generate card", ex);
		}
		finally
		{
			foreach (var d in disposables) d.Dispose();
		}
	}
}
```

## Contributor Checklist

When adding a new card renderer:

1. Implement `ICardService<TData>` with strongly typed `TData`.
2. Decide if startup preload is needed.
3. If yes, implement `IAsyncInitializable` and register with `RegisterAsyncInitializableFor`.
4. Register service as singleton in game/common application service extension.
5. Ensure calling application service provides all required context parameters.
6. Dispose all temporary image resources.
7. Throw `CommandException` on render failures.
8. Validate output dimensions and readability across expected data ranges.
