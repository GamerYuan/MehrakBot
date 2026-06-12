using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using OpenCvSharp;
using OpenCvSharp.Dnn;

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

public class NsfwClassifier : INsfwClassifier, IDisposable
{
    private readonly Net m_Net;
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

        m_Net = CvDnn.ReadNetFromOnnx(modelPath) ?? throw new InvalidOperationException("Failed to load ONNX model.");
        m_Net.SetPreferableBackend(Backend.OPENCV);
        m_Net.SetPreferableTarget(Target.CPU);

        m_Logger.LogInformation("NSFW classifier loaded from {ModelPath} with threshold {Threshold}",
            modelPath, m_NsfwThreshold);
    }

    public NsfwClassificationResult Classify(byte[] imageData)
    {
        var image = Cv2.ImDecode(imageData, ImreadModes.Color);
        if (image.Empty())
            throw new ArgumentException("Invalid image data.");

        try
        {
            // Create blob: resize to 384x384, scale to [0,1], subtract mean=0.5
            // Result: (pixel/255 - 0.5) in range [-0.5, 0.5]
            var blob = CvDnn.BlobFromImage(
                image,
                1.0 / 255.0,
                new Size(InputSize, InputSize),
                new Scalar(0.5, 0.5, 0.5),
                swapRB: true,
                crop: false);

            // Divide by std=0.5 (multiply by 2) to get range [-1, 1]
            var normalized = new Mat();
            Cv2.Multiply(blob, new Scalar(2.0), normalized);

            m_Net.SetInput(normalized);
            var output = m_Net.Forward();

            // Output shape: [1, 2] — [NSFW, SFW]
            var data = new float[2];
            Marshal.Copy(output.Data, data, 0, 2);

            // Apply softmax
            var exp0 = MathF.Exp(data[0]);
            var exp1 = MathF.Exp(data[1]);
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
        m_Net?.Dispose();
    }
}
