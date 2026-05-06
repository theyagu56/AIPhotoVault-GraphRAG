using PhotoVault.Core.Domain;

namespace PhotoVault.Core.Interfaces;

public interface IAIService
{
    string ModelName { get; }

    Task<AITagResult> TagAsync(string absoluteImagePath, CancellationToken ct = default);
    Task<string> CaptionAsync(string absoluteImagePath, CancellationToken ct = default);
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
    Task<bool> IsBlurryAsync(string absoluteImagePath, CancellationToken ct = default);
    Task<IReadOnlyList<FaceDetection>> DetectFacesAsync(string absoluteImagePath, CancellationToken ct = default);
}

public record AITagResult(IReadOnlyList<TagPrediction> Tags, string? Caption);
public record TagPrediction(string Name, TagCategory Category, double Confidence);
public record FaceDetection(BoundingBox BoundingBox, float[] Embedding, double Confidence);
