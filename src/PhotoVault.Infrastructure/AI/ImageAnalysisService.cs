using System.Numerics;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Microsoft.Extensions.Logging;
using PhotoVault.Core.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PhotoVault.Infrastructure.AI;

/// <summary>
/// Fully local image analysis — no API calls, no cost.
///  • Blur score  : Laplacian variance on grayscale image
///  • dHash       : 64-bit difference hash for near-duplicate detection
///  • EXIF        : date, GPS, camera via MetadataExtractor
/// </summary>
public sealed class ImageAnalysisService : IImageAnalysisService
{
    private readonly ILogger<ImageAnalysisService> _log;

    // Blur threshold used for logging guidance; callers can use their own.
    private const double BlurThreshold = 100.0;

    public ImageAnalysisService(ILogger<ImageAnalysisService> log) => _log = log;

    // ── Blur Score ────────────────────────────────────────────────────────────
    public async Task<double> ComputeBlurScoreAsync(string absolutePath,
                                                     CancellationToken ct = default)
    {
        try
        {
            using var image = await Image.LoadAsync<L8>(absolutePath, ct);

            // Resize to max 512px on the long edge for speed
            var (w, h) = ScaledSize(image.Width, image.Height, 512);
            image.Mutate(x => x.Resize(w, h));

            // Laplacian kernel: [0,1,0 / 1,-4,1 / 0,1,0]
            double sumSq = 0;
            double sum   = 0;
            int    count = 0;

            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    double lap = -4.0 * image[x, y].PackedValue
                                 + image[x - 1, y].PackedValue
                                 + image[x + 1, y].PackedValue
                                 + image[x, y - 1].PackedValue
                                 + image[x, y + 1].PackedValue;
                    sumSq += lap * lap;
                    sum   += lap;
                    count++;
                }
            }

            if (count == 0) return 0;
            double mean     = sum / count;
            double variance = sumSq / count - mean * mean;
            return Math.Max(0, variance);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Blur score failed for {Path}", absolutePath);
            return 0;
        }
    }

    // ── dHash ─────────────────────────────────────────────────────────────────
    public async Task<string> ComputeDHashAsync(string absolutePath,
                                                 CancellationToken ct = default)
    {
        try
        {
            using var image = await Image.LoadAsync<L8>(absolutePath, ct);
            image.Mutate(x => x.Resize(9, 8));   // 9 wide → 8 horizontal comparisons

            ulong hash = 0;
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    if (image[x, y].PackedValue > image[x + 1, y].PackedValue)
                        hash |= 1UL << (y * 8 + x);
                }
            }
            return hash.ToString("x16");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "dHash failed for {Path}", absolutePath);
            return "0000000000000000";
        }
    }

    // ── Hamming Distance ──────────────────────────────────────────────────────
    public int HammingDistance(string hashA, string hashB)
    {
        if (!ulong.TryParse(hashA, System.Globalization.NumberStyles.HexNumber, null, out var a))
            return 64;
        if (!ulong.TryParse(hashB, System.Globalization.NumberStyles.HexNumber, null, out var b))
            return 64;
        return BitOperations.PopCount(a ^ b);
    }

    // ── EXIF Extraction ───────────────────────────────────────────────────────
    public Task<ExifData> ExtractExifAsync(string absolutePath, CancellationToken ct = default)
    {
        try
        {
            var dirs = ImageMetadataReader.ReadMetadata(absolutePath);

            // Date
            DateTime? dateTaken = null;
            var ifd0  = dirs.OfType<ExifIfd0Directory>().FirstOrDefault();
            var subIfd = dirs.OfType<ExifSubIfdDirectory>().FirstOrDefault();

            if (subIfd?.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dto) == true)
                dateTaken = DateTime.SpecifyKind(dto, DateTimeKind.Local).ToUniversalTime();
            else if (ifd0?.TryGetDateTime(ExifDirectoryBase.TagDateTime, out var dt) == true)
                dateTaken = DateTime.SpecifyKind(dt, DateTimeKind.Local).ToUniversalTime();

            // GPS
            double? lat = null, lon = null;
            var gps = dirs.OfType<GpsDirectory>().FirstOrDefault();
            if (gps is not null)
            {
                var geoLoc = gps.GetGeoLocation();
                if (geoLoc is not null)
                {
                    lat = geoLoc.Latitude;
                    lon = geoLoc.Longitude;
                }
            }

            // Camera
            string? camera = ifd0?.GetDescription(ExifDirectoryBase.TagModel)?.Trim();

            // Dimensions
            int? width  = null, height = null;
            if (subIfd?.TryGetInt32(ExifDirectoryBase.TagImageWidth,  out var w) == true) width  = w;
            if (subIfd?.TryGetInt32(ExifDirectoryBase.TagImageHeight, out var h) == true) height = h;

            // MIME type from extension
            var mime = Path.GetExtension(absolutePath).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png"  => "image/png",
                ".gif"  => "image/gif",
                ".webp" => "image/webp",
                ".heic" => "image/heic",
                ".mp4"  => "video/mp4",
                ".mov"  => "video/quicktime",
                ".avi"  => "video/avi",
                _       => "application/octet-stream"
            };

            return Task.FromResult(new ExifData(dateTaken, lat, lon, camera, width, height, mime));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "EXIF extraction failed for {Path}", absolutePath);
            return Task.FromResult(new ExifData(null, null, null, null, null, null, null));
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static (int w, int h) ScaledSize(int origW, int origH, int maxDim)
    {
        if (origW <= maxDim && origH <= maxDim) return (origW, origH);
        double scale = Math.Min((double)maxDim / origW, (double)maxDim / origH);
        return ((int)(origW * scale), (int)(origH * scale));
    }
}
