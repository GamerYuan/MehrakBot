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

public sealed class NsfwClassifier : INsfwClassifier, IDisposable
{
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
        using var image = Cv2.ImDecode(imageData, ImreadModes.Color);
        if (image.Empty())
            throw new ArgumentException("Invalid image data.");

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

    public void Dispose()
    {
        m_Session?.Dispose();
    }
}
