using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace Mehrak.ImageProcessor.Shared.Services;

public class NsfwClassifierOptions
{
    public string ModelPath { get; set; } = "Assets/Models/nsfw-classifier/model.onnx";
    public float NsfwThreshold { get; set; } = 0.7f;
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

    // ViT-Tiny Patch16 384 preprocessing: (x - 0.5) / 0.5
    private const int InputSize = 384;

    public NsfwClassifier(IOptions<NsfwClassifierOptions> options, ILogger<NsfwClassifier> logger)
    {
        m_Logger = logger;
        m_NsfwThreshold = options.Value.NsfwThreshold;

        var modelPath = Path.GetFullPath(options.Value.ModelPath);
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

        try
        {
            // Resize to model input size
            using var resized = new Mat();
            Cv2.Resize(image, resized, new Size(InputSize, InputSize), interpolation: InterpolationFlags.Cubic);

            // Convert to NCHW float tensor: [1, 3, 384, 384]
            // Normalize: (pixel / 255.0 - 0.5) / 0.5 = pixel / 127.5 - 1.0
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

                    // BGR to RGB, normalize to [-1, 1]
                    tensor[0, 0, y, x] = r / 127.5f - 1.0f;
                    tensor[0, 1, y, x] = g / 127.5f - 1.0f;
                    tensor[0, 2, y, x] = b / 127.5f - 1.0f;
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

            // Output shape: [1, 2] — [NSFW, SFW]
            // Apply softmax
            var exp0 = MathF.Exp(output[0]);
            var exp1 = MathF.Exp(output[1]);
            var sum = exp0 + exp1;
            var sfwProb = exp1 / sum;
            var nsfwProb = exp0 / sum;

            var isNsfw = nsfwProb >= m_NsfwThreshold;

            return new NsfwClassificationResult(isNsfw, nsfwProb, sfwProb);
        }
        finally
        {
            image.Dispose();
        }
    }

    public void Dispose()
    {
        m_Session?.Dispose();
    }
}
