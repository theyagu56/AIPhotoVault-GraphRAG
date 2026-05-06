using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoVault.Core.Interfaces;
using PhotoVault.Core.Pipeline;
using PhotoVault.Infrastructure.FileSystem;

namespace PhotoVault.BackgroundServices;

/// <summary>
/// Runs on startup to:
///   1. Scan /PhotosVideos/ for any files not yet in the DB (first-boot / manual copy)
///   2. Watch for new files added at runtime via FileSystemWatcher
/// </summary>
public sealed class FileWatcherService : BackgroundService
{
    private readonly IFileStorageService        _storage;
    private readonly IMediaRepository           _mediaRepo;
    private readonly IMediaProcessingQueue      _queue;
    private readonly MediaRootOptions           _opts;
    private readonly ILogger<FileWatcherService> _log;

    private static readonly ProcessingStage[] AllStages =
    [
        ProcessingStage.Thumbnail,
        ProcessingStage.Metadata,
        ProcessingStage.DupeCheck,
        ProcessingStage.BlurCheck,
        ProcessingStage.Tagging,
        ProcessingStage.FaceDetection,
        ProcessingStage.Embedding
    ];

    public FileWatcherService(
        IFileStorageService   storage,
        IMediaRepository      mediaRepo,
        IMediaProcessingQueue queue,
        IOptions<MediaRootOptions> opts,
        ILogger<FileWatcherService> log)
    {
        _storage   = storage;
        _mediaRepo = mediaRepo;
        _queue     = queue;
        _opts      = opts.Value;
        _log       = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("📁 FileWatcherService starting. MediaRoot: {Root}", _opts.MediaRoot);

        // Phase A: Initial scan (catches files copied manually while app was offline)
        await RunInitialScanAsync(stoppingToken);

        // Phase B: Live watching for new files
        await WatchForNewFilesAsync(stoppingToken);
    }

    // ────────────────────────────────────────────────────────────
    // PHASE A — Initial Scan
    // ────────────────────────────────────────────────────────────
    private async Task RunInitialScanAsync(CancellationToken ct)
    {
        _log.LogInformation("🔍 Starting initial library scan...");
        int discovered = 0, queued = 0;

        await foreach (var relativePath in _storage.EnumerateMediaFilesAsync(ct))
        {
            discovered++;
            try
            {
                // Already in DB? Skip.
                var existing = await _mediaRepo.GetByPathAsync(relativePath, ct);
                if (existing is not null) continue;

                // Hash check for duplicates
                var absPath = _storage.Resolve(relativePath);
                var hash    = await _storage.ComputeHashAsync(absPath, ct);
                if (await _mediaRepo.ExistsByHashAsync(hash, ct))
                {
                    _log.LogDebug("Duplicate skipped: {Path}", relativePath);
                    continue;
                }

                // Create stub DB record — pipeline fills in the rest
                var media = new Core.Domain.Media
                {
                    FileName     = Path.GetFileName(relativePath),
                    OriginalPath = relativePath,
                    FileHash     = hash,
                    MediaType    = DetectMediaType(relativePath),
                    FileSizeBytes = new FileInfo(absPath).Length,
                };
                var id = await _mediaRepo.InsertAsync(media, ct);

                await _queue.EnqueueAsync(MediaProcessingJob.Create(id, absPath, AllStages), ct);
                queued++;

                if (queued % 100 == 0)
                    _log.LogInformation("📦 Scan progress: {Queued} queued / {Found} found", queued, discovered);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Scan error on {Path}", relativePath);
            }
        }

        _log.LogInformation("✅ Initial scan complete. {D} files found, {Q} queued for processing.",
                            discovered, queued);
    }

    // ────────────────────────────────────────────────────────────
    // PHASE B — Live File System Watcher
    // ────────────────────────────────────────────────────────────
    private async Task WatchForNewFilesAsync(CancellationToken ct)
    {
        var photosRoot = _storage.Resolve("PhotosVideos");

        using var watcher = new FileSystemWatcher(photosRoot)
        {
            IncludeSubdirectories  = true,
            EnableRaisingEvents    = false,
            NotifyFilter           = NotifyFilters.FileName | NotifyFilters.Size,
            Filter                 = "*.*"
        };

        // Buffer events to avoid duplicate triggers (copy = Created + multiple Changed)
        var pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var debounce = new System.Timers.Timer(3_000) { AutoReset = false };

        watcher.Created += (_, e) => { lock (pending) pending.Add(e.FullPath); debounce.Stop(); debounce.Start(); };
        watcher.Renamed += (_, e) => { lock (pending) pending.Add(e.FullPath); debounce.Stop(); debounce.Start(); };

        debounce.Elapsed += async (_, _) =>
        {
            string[] files;
            lock (pending) { files = [.. pending]; pending.Clear(); }
            foreach (var f in files) await HandleNewFileAsync(f, ct);
        };

        watcher.EnableRaisingEvents = true;
        _log.LogInformation("👁  Watching: {Root}", photosRoot);

        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
    }

    private async Task HandleNewFileAsync(string absolutePath, CancellationToken ct)
    {
        try
        {
            if (!IsSupportedMedia(absolutePath)) return;
            if (absolutePath.EndsWith(".photovault-deleted", StringComparison.OrdinalIgnoreCase)) return;

            // Wait until file is fully written (up to 10s)
            await WaitUntilFileReadyAsync(absolutePath, ct);

            var relative = Path.GetRelativePath(_opts.MediaRoot, absolutePath);
            var existing = await _mediaRepo.GetByPathAsync(relative, ct);
            if (existing is not null) return;

            var hash = await _storage.ComputeHashAsync(absolutePath, ct);
            if (await _mediaRepo.ExistsByHashAsync(hash, ct))
            {
                _log.LogInformation("Duplicate detected (live): {File}", absolutePath);
                return;
            }

            var media = new Core.Domain.Media
            {
                FileName      = Path.GetFileName(absolutePath),
                OriginalPath  = relative,
                FileHash      = hash,
                MediaType     = DetectMediaType(absolutePath),
                FileSizeBytes = new FileInfo(absolutePath).Length,
            };
            var id = await _mediaRepo.InsertAsync(media, ct);
            await _queue.EnqueueAsync(MediaProcessingJob.Create(id, absolutePath, AllStages), ct);
            _log.LogInformation("➕ New file queued: {File}", relative);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Error handling new file: {Path}", absolutePath);
        }
    }

    // ────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────
    private static readonly HashSet<string> PhotoExts = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".heic", ".gif", ".webp", ".bmp", ".tiff" };
    private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mov", ".avi", ".mkv", ".m4v", ".wmv" };

    private static bool IsSupportedMedia(string path)
    {
        var ext = Path.GetExtension(path);
        return PhotoExts.Contains(ext) || VideoExts.Contains(ext);
    }

    private static Core.Domain.MediaType DetectMediaType(string path)
    {
        var ext = Path.GetExtension(path);
        if (PhotoExts.Contains(ext)) return Core.Domain.MediaType.Photo;
        if (VideoExts.Contains(ext)) return Core.Domain.MediaType.Video;
        return Core.Domain.MediaType.Unknown;
    }

    private static async Task WaitUntilFileReadyAsync(string path, CancellationToken ct)
    {
        for (int i = 0; i < 20; i++)  // up to 10 seconds
        {
            try { using var _ = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None); return; }
            catch (IOException) { await Task.Delay(500, ct); }
        }
    }
}
