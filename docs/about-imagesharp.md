# About ImageSharp

Mehrak uses ImageSharp as the rendering backend for all the card services. In newer versions of Mehrak, we use ImageSharp v4, the latest release of ImageSharp that introduces a handful of breaking changes and performance improvements over ImageSharp v3

## Licensing

Mehrak is eligible for the community license as a hobby/open-source project. A build-time license file - `sixlabors.lic` is required to compile Mehrak locally. This license file should be placed under `MehrakBot/`, as this is the only project that directly depends on ImageSharp. More about licensing can be found here: https://sixlabors.com/posts/licence-enforcement-changes/

## API Changes

ImageSharp v4 and ImageSharp.Drawing v3 migrated to a new rendering model, where most drawing related APIs now rely on a new `DrawingCanvas` abstraction. `Image.Mutate(Action<IImageProcessingContext>)` still exists as an API to apply image-wide processors, such as resize, blur, rotate etc.

`DrawingCanvas` takes over the bulk of drawing operation, and generally follows similar API names as ImageSharp v3, eg. `DrawingCanvas.Draw` corresponds to `IImageProcessingContext.Draw [v3]`, `DrawingCanvas.DrawText` corresponds to `IImageProcessingContext.DrawText [v3]` etc.

Mehrak uses only a subset of currently available APIs, and here are some notable changes:

1. Signature changes for `Draw`, `DrawText` and `DrawImage`:

- `Draw(Pen pen, IPath path)`: You should use `Pens.[Type](Color color, float width)` factory method to instantiate the `Pen`, eg. `Pens.Solid(Color.White, 5f)`
- `DrawText(TextOptions options, ReadOnlySpan<char> text, Brush? brush, Pen? pen)`: `Brush` is for the fill of the glyph, and `Pen` is for the outline of the glyph. Specifying null for either will render the fill/outline as transparent. You should use `Brushes.[Type](Color color)` factory method to instantiate `Brush`, and similarly use the factory method for `Pen`
- `DrawImage(Image source, Rectangle srcRect, RectangleF destRect, IResampler? resampler)`: The DrawImage API now automatically resizes the image from the source to the destination size, based on the specified resampler. In general, to draw the full image, we can use `source.Bounds` as srcRect, and specify the destination. You should prefer `KnownResamplers.Bicubic` for the resampler

2. Color API changes: To declare a Color with values, you must now use `Color.FromPixel<TPixel>(TPixel color)`, eg. `Color color = Color.FromPixel(new Rgba32(69, 69, 69, 255))`. To specify color to use for `Image<TPixel>`, we must use `TPixel` or `Color.ToPixel<TPixel>()`, eg. `Image<Rgba32> image = new(width, height, Color.Transparent.ToPixel<Rgba32>())`. Static pre-defined colors, such as `Color.Red`, `Color.Blue` etc. remains the same, and follows the same naming as W3C

3. `TextMeasurer.MeasureSize` no longer exists, but we can directly substitute with `TextMeasurer.MeasureBounds`, with the same argument list

There are other APIs that are introduced for `DrawingCanvas`, which changes the way how we can use the drawing APIs more efficiently. The new `DrawingCanvas` acts kind of similar to an immediate mode rendering API (similar to legacy OpenGL), but all the operations are saved on a playback timeline, which gets replayed and rendered accordingly when the canvas is disposed of, this meant that references held by operations in this replay timeline must remain in memory until the canvas has been disposed of (where the operations are replayed), this includes references to `Image`, `Font`, among other `IDisposable`. With this in mind, here are a list of them and how we could use it

1. `int DrawingCanvas.Save()`, `int DrawingCanvas.Save(GraphicsOptions options, params IPath[] clipPath)`. `Save` allows us to define a new drawing state (stored in a stack), where all future operations after this `Save` will have the corresponding `GraphicsOptions` applied, which could include things like `AffineTransform`, `AntiAliasing` etc. If a `clipPath` is specified, then all future operations will be clipped into this path accordingly, the behaviour is defined by `GraphicsOptions.ShapeOptions.BooleanOperation`, where the default is `BooleanOperation.Difference`, which subtracts the operations from the clipping path. We an use `BooleanOperation.Intersection` to clip the drawing operations to be inside the clipping path. To restore the drawing state to a previous one, we can use `DrawingCanvas.Restore()`, which pops the topmost state, or `DrawingCanvas.RestoreTo(int)`, which restores all the state until the specific state (but it will retain the specified state), the state number can be saved from the return value of `Save`

2. `int DrawingCanvas.SaveLayer()`, `int DrawingCanvas.SaveLayer(GraphicsOptions options)`, `int DrawingCanvas.SaveLayer(GraphicsOptions options, Rectangle bounds)`. Similar to `Save`, this defines a new drawing state, but this "layer" is an isolated compositing layer, where all the operations will first be done in isolation before being composited back to the canvas when the layer is restored with `DrawingCanvas.Restore()`. The `bounds` define the local bounds of the layer, where only this region of the whole canvas will be allocated and composited back

3. `DrawingCanvas DrawingCanvas.CreateRegion(Rectangle region)`. This defines a new child region with its own local coordinates (starting with (0, 0) on the top left corner of this child region), operations cannot exceed the bounds of this region, or the backend will throw a IOOB exception. This is useful when we want to only draw on a small region of the main drawing canvas, while also having the convenience of a local coordinate system, eg. for a module

4. `DrawingCanvas.Apply(IPath path, Action<IImageProcessingContext> operation)`, `DrawingCanvas.Apply(PathBuilder pathBuilder, Action<IImageProcessingContext> operation)`, `DrawingCanvas.Apply(Rectangle region, Action<IImageProcessingContext> operation)`. As mentioned, the canvas operations are recorded in a timeline, but the parent `IImageProcessingContext` is immediate. This meant that in order to apply image processing operations, eg. blur, to a sub region of the canvas, we must force the `IImageProcessingContext` to observe that the canvas has the previous changes applied. This API provides this utility to allow the `IImageProcessingContext` to observe the changes made to the canvas during playback, and apply the operation on the specified region

The full method list can be found here https://docs.sixlabors.com/api/ImageSharp.Drawing/SixLabors.ImageSharp.Drawing.Processing.DrawingCanvas.html, currently there are no built-in extension methods for `DrawingCanvas`

## Known Bugs

There is a bug whereby if we call `DrawText` after a `Save` that defines a clipping path, the glyphs doesn't render properly. We can work around this by restoring the draw state, draw the text accordingly, then call `Save` with the same clipping path again
