using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace Mehrak.ImageProcessor.Shared.Services;

public class NsfwClassifierOptions
{
    public string ModelPath { get; set; } = "Assets/Models/nsfw-classifier/freepik-nsfw-model.onnx";
    public float NsfwThreshold { get; set; } = 0.5f;
}

public interface INsfwClassifier
{
    NsfwClassificationResult Classify(byte[] imageData);
}

public record NsfwClassificationResult(bool IsNsfw, float NsfwConfidence, float SfwConfidence);

/// <summary>
/// Image geometry read from container headers without allocating pixels.
/// </summary>
public readonly record struct UploadImageGeometry(string Format, int Width, int Height, int Frames);

public sealed class NsfwClassifier : INsfwClassifier, IDisposable
{
    // Transport bytes are already capped by gRPC limits, but a small payload can still
    // describe a huge image. These budgets bound native decode/inference allocations.
    public const int MaxImageBytes = 8 * 1024 * 1024; // 8 MB, matches the Dashboard upload cap
    public const int MaxImageWidth = 4096;
    public const int MaxImageHeight = 4096;
    public const long MaxImagePixels = 16_777_216; // 4096 x 4096
    public const int MaxImageFrames = 1;

    private const int MaxConcurrentClassifications = 2;
    private static readonly TimeSpan ClassificationWaitTimeout = TimeSpan.FromSeconds(30);
    private static readonly SemaphoreSlim s_ClassificationGate = new(MaxConcurrentClassifications, MaxConcurrentClassifications);

    private readonly InferenceSession m_Session;
    private readonly float m_NsfwThreshold;
    private readonly ILogger<NsfwClassifier> m_Logger;

    // EVA02 Base Patch14 448 preprocessing: (x/255 - mean) / std
    private const int InputSize = 448;

    private static readonly float[] Mean = [0.48145466f, 0.4578275f, 0.40821073f];
    private static readonly float[] Std = [0.26862954f, 0.26130258f, 0.27577711f];

    public NsfwClassifier(IOptions<NsfwClassifierOptions> options, ILogger<NsfwClassifier> logger)
    {
        m_Logger = logger;
        m_NsfwThreshold = options.Value.NsfwThreshold;

        var modelPath = Path.Combine(AppContext.BaseDirectory, options.Value.ModelPath);
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"NSFW classification model not found at: {modelPath}");

        m_Session = new InferenceSession(modelPath);

        m_Logger.LogInformation("NSFW classifier loaded from {ModelPath} with threshold {Threshold}",
            modelPath, m_NsfwThreshold);
    }

    public NsfwClassificationResult Classify(byte[] imageData)
    {
        if (imageData is null || imageData.Length == 0)
            throw new ArgumentException("Image data is empty.");

        if (imageData.Length > MaxImageBytes)
            throw new ArgumentException($"Image payload exceeds the {MaxImageBytes} byte limit.");

        // Pre-decode budget check from container headers only (no pixel allocation).
        // Unknown containers skip this check and rely on the post-decode verification.
        if (TryGetImageGeometry(imageData, out var headerGeometry))
            ValidatePixelBudget(headerGeometry);

        if (!s_ClassificationGate.Wait(ClassificationWaitTimeout))
            throw new InvalidOperationException("Image classification is busy. Try again later.");

        try
        {
            return ClassifyCore(imageData);
        }
        finally
        {
            s_ClassificationGate.Release();
        }
    }

    /// <summary>
    /// Enforces the pixel budget for a decoded or header-parsed geometry.
    /// Throws <see cref="ArgumentException"/> when any bound is exceeded.
    /// </summary>
    public static void ValidatePixelBudget(UploadImageGeometry geometry)
    {
        if (geometry.Width <= 0 || geometry.Height <= 0)
            throw new ArgumentException("Image has invalid dimensions.");

        if (geometry.Width > MaxImageWidth || geometry.Height > MaxImageHeight)
            throw new ArgumentException(
                $"Image dimensions {geometry.Width}x{geometry.Height} exceed the {MaxImageWidth}x{MaxImageHeight} limit.");

        if ((long)geometry.Width * geometry.Height > MaxImagePixels)
            throw new ArgumentException($"Image exceeds the {MaxImagePixels} pixel budget.");

        if (geometry.Frames > MaxImageFrames)
            throw new ArgumentException("Animated images are not allowed.");
    }

    /// <summary>
    /// Reads format, dimensions, and frame count from PNG/JPEG/GIF/BMP/WebP container
    /// headers without decoding pixels. Returns false for unknown containers.
    /// </summary>
    public static bool TryGetImageGeometry(byte[] data, out UploadImageGeometry geometry)
    {
        try
        {
            if (IsPng(data))
                return TryReadPngGeometry(data, out geometry);
            if (IsJpeg(data))
                return TryReadJpegGeometry(data, out geometry);
            if (IsGif(data))
                return TryReadGifGeometry(data, out geometry);
            if (IsBmp(data))
                return TryReadBmpGeometry(data, out geometry);
            if (IsWebP(data))
                return TryReadWebPGeometry(data, out geometry);
        }
        catch
        {
            // Fall through to unknown-container handling below.
        }

        geometry = default;
        return false;
    }

    private NsfwClassificationResult ClassifyCore(byte[] imageData)
    {
        using var image = Cv2.ImDecode(imageData, ImreadModes.Color);
        if (image.Empty())
            throw new ArgumentException("Invalid image data.");

        // Post-decode verification: headers can lie (or be absent for unknown
        // containers), so re-check the actual decoded geometry before allocating
        // anything else on the native heap.
        ValidatePixelBudget(new UploadImageGeometry("decoded", image.Width, image.Height, 1));

        // Resize to model input size
        using var resized = new Mat();
        Cv2.Resize(image, resized, new Size(InputSize, InputSize), interpolation: InterpolationFlags.Cubic);

        // Convert to NCHW float tensor: [1, 3, 448, 448]
        // Normalize: (pixel / 255.0 - mean) / std
        var tensor = new DenseTensor<float>([1, 3, InputSize, InputSize]);

        // Extract pixel data from Mat (BGR format)
        var pixelData = new byte[InputSize * InputSize * 3];
        System.Runtime.InteropServices.Marshal.Copy(resized.Data, pixelData, 0, pixelData.Length);

        for (var y = 0; y < InputSize; y++)
        {
            for (var x = 0; x < InputSize; x++)
            {
                var pixelOffset = (y * InputSize + x) * 3;
                var b = pixelData[pixelOffset];
                var g = pixelData[pixelOffset + 1];
                var r = pixelData[pixelOffset + 2];

                // BGR to RGB, normalize with ImageNet mean/std
                tensor[0, 0, y, x] = (r / 255.0f - Mean[0]) / Std[0];
                tensor[0, 1, y, x] = (g / 255.0f - Mean[1]) / Std[1];
                tensor[0, 2, y, x] = (b / 255.0f - Mean[2]) / Std[2];
            }
        }

        // Get input name
        var inputName = m_Session.InputMetadata.Keys.First();

        // Run inference
        var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputName, tensor)
            };

        using var results = m_Session.Run(inputs);
        var output = results[0].AsEnumerable<float>().ToArray();

        // Output shape: [1, 4] — [neutral, low, medium, high]
        // Apply softmax
        var maxVal = output.Max();
        var exps = output.Select(v => MathF.Exp(v - maxVal)).ToArray();
        var sum = exps.Sum();
        var probs = exps.Select(e => e / sum).ToArray();

        var nsfwProb = probs[3]; // "high" class
        var sfwProb = probs[0] + probs[1] + probs[2]; // neutral + low + medium

        var isNsfw = nsfwProb >= m_NsfwThreshold;

        return new NsfwClassificationResult(isNsfw, nsfwProb, sfwProb);

    }

    private const int HeaderScanLimit = 1 << 20; // 1 MB is plenty for dimension headers

    private static bool IsPng(byte[] data) =>
        data.Length >= 8 &&
        data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
        data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A;

    private static bool IsJpeg(byte[] data) =>
        data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8;

    private static bool IsGif(byte[] data) =>
        data.Length >= 6 &&
        data[0] == 'G' && data[1] == 'I' && data[2] == 'F' &&
        data[3] == '8' && (data[4] == '7' || data[4] == '9') && data[5] == 'a';

    private static bool IsBmp(byte[] data) =>
        data.Length >= 2 && data[0] == 'B' && data[1] == 'M';

    private static bool IsWebP(byte[] data) =>
        data.Length >= 12 &&
        data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' &&
        data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P';

    private static bool TryReadPngGeometry(byte[] data, out UploadImageGeometry geometry)
    {
        geometry = default;
        // Signature(8) + IHDR length(4) + "IHDR" + width(4 BE) + height(4 BE).
        if (data.Length < 33 || !ChunkTypeIs(data, 12, "IHDR"))
            return false;

        var width = ReadUInt32BE(data, 16);
        var height = ReadUInt32BE(data, 20);
        if (width == 0 || height == 0)
            return false;

        // Animated PNG carries an acTL chunk before IDAT; each animation frame would
        // otherwise decode into additional native allocations downstream.
        var frames = 1;
        var offset = 8;
        var end = Math.Min(data.Length, HeaderScanLimit);
        while (offset + 8 <= end)
        {
            var length = ReadUInt32BE(data, offset);
            if (ChunkTypeIs(data, offset + 4, "acTL"))
            {
                frames = 2;
                break;
            }
            if (ChunkTypeIs(data, offset + 4, "IDAT") || ChunkTypeIs(data, offset + 4, "IEND"))
                break;
            if (length > (uint)(end - offset - 12))
                break;
            offset += 12 + (int)length;
        }

        geometry = new UploadImageGeometry("png", Saturate(width), Saturate(height), frames);
        return true;
    }

    private static bool TryReadJpegGeometry(byte[] data, out UploadImageGeometry geometry)
    {
        geometry = default;
        var offset = 2; // skip SOI
        var end = Math.Min(data.Length, HeaderScanLimit);
        while (offset + 4 <= end)
        {
            if (data[offset] != 0xFF)
                return false;
            var marker = data[offset + 1];
            while (marker == 0xFF && offset + 2 <= end)
            {
                offset++;
                marker = data[offset + 1];
            }
            // Standalone markers carry no length field.
            if (marker == 0xD8 || marker == 0xD9 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                offset += 2;
                continue;
            }
            if (offset + 4 > end)
                return false;
            var segmentLength = (data[offset + 2] << 8) | data[offset + 3];
            if (segmentLength < 2)
                return false;
            if (IsStartOfFrame(marker))
            {
                if (offset + 9 > end)
                    return false;
                var height = (data[offset + 5] << 8) | data[offset + 6];
                var width = (data[offset + 7] << 8) | data[offset + 8];
                if (width == 0 || height == 0)
                    return false;
                geometry = new UploadImageGeometry("jpeg", width, height, 1);
                return true;
            }
            offset += 2 + segmentLength;
        }
        return false;
    }

    private static bool IsStartOfFrame(byte marker) => marker switch
    {
        0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or
        0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF => true,
        _ => false,
    };

    private static bool TryReadGifGeometry(byte[] data, out UploadImageGeometry geometry)
    {
        geometry = default;
        if (data.Length < 10)
            return false;

        var width = data[6] | (data[7] << 8);
        var height = data[8] | (data[9] << 8);
        if (width == 0 || height == 0)
            return false;

        // Each animation frame is introduced by a Graphic Control Extension block.
        var frames = 1;
        var end = Math.Min(data.Length - 1, HeaderScanLimit);
        for (var i = 0; i < end; i++)
        {
            if (data[i] == 0x21 && data[i + 1] == 0xF9)
                frames++;
        }

        geometry = new UploadImageGeometry("gif", width, height, frames);
        return true;
    }

    private static bool TryReadBmpGeometry(byte[] data, out UploadImageGeometry geometry)
    {
        geometry = default;
        if (data.Length < 26)
            return false;

        var width = BitConverter.ToInt32(data, 18);
        var height = Math.Abs(BitConverter.ToInt32(data, 22));
        if (width <= 0 || height <= 0)
            return false;

        geometry = new UploadImageGeometry("bmp", width, height, 1);
        return true;
    }

    private static bool TryReadWebPGeometry(byte[] data, out UploadImageGeometry geometry)
    {
        geometry = default;
        // Chunk data starts at offset 20: "VP8X"/"VP8 "/"VP8L" header at offset 12.
        if (data.Length < 30)
            return false;

        if (ChunkTypeIs(data, 12, "VP8X"))
        {
            if (data.Length < 32)
                return false;
            var width = ReadUInt24LE(data, 24) + 1;
            var height = ReadUInt24LE(data, 27) + 1;
            var animated = (data[20] & 0x02) != 0;
            geometry = new UploadImageGeometry("webp", width, height, animated ? 2 : 1);
            return width > 0 && height > 0;
        }

        if (ChunkTypeIs(data, 12, "VP8 "))
        {
            if (data.Length < 30)
                return false;
            var width = data[26] | ((data[27] & 0x3F) << 8);
            var height = data[28] | ((data[29] & 0x3F) << 8);
            if (width == 0 || height == 0)
                return false;
            geometry = new UploadImageGeometry("webp", width, height, 1);
            return true;
        }

        if (ChunkTypeIs(data, 12, "VP8L"))
        {
            if (data.Length < 25 || data[20] != 0x2F)
                return false;
            var width = (data[21] | ((data[22] & 0x3F) << 8)) + 1;
            var height = (((data[22] >> 6) & 0x03) | (data[23] << 2) | ((data[24] & 0x0F) << 10)) + 1;
            if (width <= 0 || height <= 0)
                return false;
            geometry = new UploadImageGeometry("webp", width, height, 1);
            return true;
        }

        return false;
    }

    private static bool ChunkTypeIs(byte[] data, int offset, string type) =>
        offset + 4 <= data.Length &&
        data[offset] == type[0] && data[offset + 1] == type[1] &&
        data[offset + 2] == type[2] && data[offset + 3] == type[3];

    private static uint ReadUInt32BE(byte[] data, int offset) =>
        ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
        ((uint)data[offset + 2] << 8) | data[offset + 3];

    private static int ReadUInt24LE(byte[] data, int offset) =>
        data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16);

    private static int Saturate(uint value) =>
        value > int.MaxValue ? int.MaxValue : (int)value;

    public void Dispose()
    {
        m_Session?.Dispose();
    }
}
