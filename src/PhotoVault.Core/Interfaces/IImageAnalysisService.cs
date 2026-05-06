namespace PhotoVault.Core.Interfaces;

/// <summary>
/// Local image analysis — no API calls, no cost.
/// Blur detection via Laplacian variance; duplicate fingerprinting via dHash.
/// </summary>
public interface IImageAnalysisService
{
    /// <summary>
    /// Compute Laplacian variance of the image.
    /// Lower score = blurrier. Typical threshold: &lt; 100 → blurry.
    /// </summary>
    Task<double> ComputeBlurScoreAsync(string absolutePath, CancellationToken ct = default);

    /// <summary>
    /// Compute a 64-bit difference hash (dHash) for near-duplicate detection.
    /// Returns hex string, e.g. "a1b2c3d4e5f60718".
    /// </summary>
    Task<string> ComputeDHashAsync(string absolutePath, CancellationToken ct = default);

    /// <summary>
    /// Hamming distance between two hex dHash strings (0 = identical, 64 = opposite).
    /// Threshold ≤ 8 is considered a near-duplicate.
    /// </summary>
    int HammingDistance(string hashA, string hashB);

    /// <summary>
    /// Extract key EXIF fields from an image file.
    /// </summary>
    Task<ExifData> ExtractExifAsync(string absolutePath, CancellationToken ct = default);
}

public record ExifData(
    DateTime? DateTaken,
    double?   Latitude,
    double?   Longitude,
    string?   CameraModel,
    int?      Width,
    int?      Height,
    string?   MimeType
);
