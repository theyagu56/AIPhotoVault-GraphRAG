using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PhotoVault.Core.Interfaces;
using PhotoVault.Core.Pipeline;
using PhotoVault.Infrastructure.AI;

namespace PhotoVault.BackgroundServices;

/// <summary>
/// Consumes jobs from IMediaProcessingQueue and runs each stage in sequence:
///   EXIF → Blur → pHash → Duplicate check → Thumbnails → GPT-4o Tagging
/// </summary>
public sealed class MediaProcessingWorker : BackgroundService
{
    private readonly IMediaProcessingQueue   _queue;
    private readonly IMediaRepository        _mediaRepo;
    private readonly ITagRepository          _tagRepo;
    private readonly IThumbnailService       _thumbs;
    private readonly IAIService              _ai;
    private readonly IImageAnalysisService   _imageAnalysis;
    private readonly GraphIndexService       _graphIndex;
    private readonly ILogger<MediaProcessingWorker> _log;

    // pHash near-duplicate threshold (Hamming distance ≤ this → duplicate)
    private const int DuplicateThreshold = 8;

    public MediaProcessingWorker(
        IMediaProcessingQueue   queue,
        IMediaRepository        mediaRepo,
        ITagRepository          tagRepo,
        IThumbnailService       thumbs,
        IAIService              ai,
        IImageAnalysisService   imageAnalysis,
        GraphIndexService       graphIndex,
        ILogger<MediaProcessingWorker> log)
    {
        _queue         = queue;
        _mediaRepo     = mediaRepo;
        _tagRepo       = tagRepo;
        _thumbs        = thumbs;
        _ai            = ai;
        _imageAnalysis = imageAnalysis;
        _graphIndex    = graphIndex;
        _log           = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("⚙️  MediaProcessingWorker started");

        await foreach (var job in DequeueAll(stoppingToken))
        {
            _log.LogInformation("⚙️  Processing {MediaId}", job.MediaId);
            foreach (var stage in job.Stages)
            {
                try   { await RunStageAsync(job, stage, stoppingToken); }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Stage {Stage} failed for {MediaId}", stage, job.MediaId);
                }
            }
        }
    }

    private async Task RunStageAsync(MediaProcessingJob job, ProcessingStage stage,
                                      CancellationToken ct)
    {
        switch (stage)
        {
            // ── 1. EXIF / Metadata ────────────────────────────────
            case ProcessingStage.Metadata:
            {
                var ext = Path.GetExtension(job.AbsolutePath).ToLowerInvariant();
                if (!IsImageFile(ext)) break;

                var exif  = await _imageAnalysis.ExtractExifAsync(job.AbsolutePath, ct);
                var media = await _mediaRepo.GetByIdAsync(job.MediaId, ct);
                if (media is null) break;

                if (exif.DateTaken.HasValue) media.CapturedAt = exif.DateTaken;
                if (exif.Latitude.HasValue)  media.Latitude   = exif.Latitude;
                if (exif.Longitude.HasValue) media.Longitude  = exif.Longitude;
                if (exif.CameraModel != null) media.CameraModel = exif.CameraModel;
                if (exif.Width.HasValue)     media.Width      = exif.Width;
                if (exif.Height.HasValue)    media.Height     = exif.Height;
                if (exif.MimeType != null)   media.MimeType   = exif.MimeType;

                await _mediaRepo.UpdateAsync(media, ct);
                _log.LogDebug("✔ EXIF: {Id} captured={Date} gps={Lat},{Lon}",
                              job.MediaId, exif.DateTaken, exif.Latitude, exif.Longitude);
                break;
            }

            // ── 2. Blur Detection ─────────────────────────────────
            case ProcessingStage.BlurCheck:
            {
                var ext = Path.GetExtension(job.AbsolutePath).ToLowerInvariant();
                if (!IsImageSharpCompatible(ext)) break;  // HEIC skipped — ImageSharp can't decode it

                var blurScore = await _imageAnalysis.ComputeBlurScoreAsync(job.AbsolutePath, ct);
                var isBlurry  = blurScore < 100.0;

                _log.LogDebug("✔ Blur: {Id} score={Score:F1} blurry={Blurry}",
                              job.MediaId, blurScore, isBlurry);

                // We'll write blur+pHash together in DupeCheck to save a DB round-trip
                job.BlurScore = blurScore;
                job.IsBlurry  = isBlurry;
                break;
            }

            // ── 3. pHash + Duplicate Check ────────────────────────
            case ProcessingStage.DupeCheck:
            {
                var ext = Path.GetExtension(job.AbsolutePath).ToLowerInvariant();
                if (!IsImageSharpCompatible(ext)) break;  // HEIC skipped

                var pHash = await _imageAnalysis.ComputeDHashAsync(job.AbsolutePath, ct);

                // Load all existing hashes from DB and compare
                var existingHashes = await _mediaRepo.GetAllPerceptualHashesAsync(ct);
                bool   isDuplicate  = false;
                string? duplicateOf = null;

                foreach (var entry in existingHashes)
                {
                    var parts = entry.Split(':', 2);
                    if (parts.Length != 2) continue;
                    var otherId   = parts[0];
                    var otherHash = parts[1];
                    if (otherId == job.MediaId) continue;

                    var dist = _imageAnalysis.HammingDistance(pHash, otherHash);
                    if (dist <= DuplicateThreshold)
                    {
                        isDuplicate = true;
                        duplicateOf = otherId;
                        _log.LogInformation("🔁 Duplicate found: {Id} ≈ {Other} (dist={D})",
                                             job.MediaId, otherId, dist);
                        break;
                    }
                }

                await _mediaRepo.UpdateAIResultsAsync(
                    job.MediaId,
                    job.BlurScore,
                    job.IsBlurry,
                    pHash,
                    isDuplicate,
                    duplicateOf,
                    ct);

                _log.LogDebug("✔ pHash+DupeCheck: {Id} dup={Dup}", job.MediaId, isDuplicate);
                break;
            }

            // ── 4. Thumbnail Generation ───────────────────────────
            case ProcessingStage.Thumbnail:
            {
                var ext = Path.GetExtension(job.AbsolutePath).ToLowerInvariant();
                if (!IsImageSharpCompatible(ext)) break;  // HEIC skipped — no ImageSharp decoder
                await _thumbs.GenerateAllSizesAsync(job.MediaId, job.AbsolutePath, ct);
                _log.LogDebug("✔ Thumbnails: {Id}", job.MediaId);
                break;
            }

            // ── 5. GPT-4o Tagging ─────────────────────────────────
            case ProcessingStage.Tagging:
            {
                var ext = Path.GetExtension(job.AbsolutePath).ToLowerInvariant();
                if (!IsImageFile(ext)) break;

                var result = await _ai.TagAsync(job.AbsolutePath, ct);

                // Persist tags
                foreach (var pred in result.Tags)
                {
                    try
                    {
                        var tag = await _tagRepo.UpsertTagAsync(
                            pred.Name, pred.Category, isAI: true, ct);
                        await _tagRepo.AddMediaTagAsync(
                            job.MediaId, tag.Id, pred.Confidence,
                            PhotoVault.Core.Domain.TagSource.AI, ct);
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Failed to save tag '{Tag}'", pred.Name);
                    }
                }

                // Persist caption
                if (!string.IsNullOrWhiteSpace(result.Caption))
                    await _tagRepo.SaveCaptionAsync(job.MediaId, result.Caption, _ai.ModelName, ct);

                // Mark as AI-processed
                await _mediaRepo.MarkAIProcessedAsync(job.MediaId, _ai.ModelName, ct);
                _log.LogInformation("✔ Tagged {Id}: {Count} tags — \"{Caption}\"",
                                     job.MediaId, result.Tags.Count, result.Caption);
                break;
            }

            // ── 6. Graph Indexing ─────────────────────────────
            case ProcessingStage.GraphIndex:
            {
                var media = await _mediaRepo.GetByIdAsync(job.MediaId, ct);
                if (media is null) break;

                await _graphIndex.IndexPhotoAsync(media, ct);
                _log.LogDebug("📊 Graph indexed: {Id}", job.MediaId);
                break;
            }

            case ProcessingStage.FaceDetection:
            case ProcessingStage.Embedding:
                _log.LogDebug("⏭ {Stage}: Phase 5", stage);
                break;
        }
    }

    // MetadataExtractor can read EXIF from all of these (incl. HEIC).
    private static bool IsImageFile(string ext)
        => new[] { ".jpg", ".jpeg", ".png", ".webp", ".heic", ".gif", ".tiff" }.Contains(ext);

    // ImageSharp cannot decode HEIC — skip pixel-level stages for it.
    private static bool IsImageSharpCompatible(string ext)
        => new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".tiff" }.Contains(ext);

    private async IAsyncEnumerable<MediaProcessingJob> DequeueAll(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            MediaProcessingJob job;
            try { job = await _queue.DequeueAsync(ct); }
            catch (OperationCanceledException) { yield break; }
            yield return job;
        }
    }
}
