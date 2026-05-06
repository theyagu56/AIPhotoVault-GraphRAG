using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoVault.Core.Interfaces;

namespace PhotoVault.Infrastructure.FileSystem;

public class LocalFileStorageService : IFileStorageService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".heic", ".gif", ".webp", ".bmp", ".tiff",
        ".mp4", ".mov", ".avi", ".mkv", ".m4v", ".wmv"
    };

    private readonly MediaRootOptions _opts;
    private readonly ILogger<LocalFileStorageService> _log;

    public LocalFileStorageService(IOptions<MediaRootOptions> opts,
                                   ILogger<LocalFileStorageService> log)
    {
        _opts = opts.Value;
        _log  = log;
    }

    // ── Resolve ────────────────────────────────────────────────
    public string Resolve(string relativePath)
        => Path.GetFullPath(Path.Combine(_opts.MediaRoot, relativePath));

    // ── Hash ──────────────────────────────────────────────────
    public async Task<string> ComputeHashAsync(string absolutePath, CancellationToken ct = default)
    {
        await using var fs = File.OpenRead(absolutePath);
        var bytes = await SHA256.HashDataAsync(fs, ct);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // ── Ingest ────────────────────────────────────────────────
    public async Task<string> IngestFileAsync(string sourcePath, string? subFolder = null,
                                              CancellationToken ct = default)
    {
        var ext = Path.GetExtension(sourcePath);
        var datePart = DateTime.UtcNow.ToString("yyyy/MM");
        var relative = Path.Combine("PhotosVideos", subFolder ?? datePart,
                                    Path.GetFileName(sourcePath));
        var dest = Resolve(relative);

        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

        if (!File.Exists(dest))
            await Task.Run(() => File.Copy(sourcePath, dest, overwrite: false), ct);

        _log.LogInformation("Ingested {File} → {Dest}", sourcePath, relative);
        return relative;
    }

    // ── Trash ─────────────────────────────────────────────────
    public async Task<string> MoveToTrashAsync(string relativePath, CancellationToken ct = default)
    {
        var source = Resolve(relativePath);
        var trashRelative = Path.Combine("Application", "Trash",
            $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Path.GetFileName(relativePath)}");
        var dest = Resolve(trashRelative);

        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        await Task.Run(() => File.Move(source, dest), ct);

        // Leave a placeholder so the path isn't a ghost
        var placeholder = source + ".photovault-deleted";
        await File.WriteAllTextAsync(placeholder,
            $"{{\"trashedAt\":\"{DateTime.UtcNow:O}\",\"trashPath\":\"{trashRelative}\"}}", ct);

        _log.LogInformation("Trashed {Path} → {Trash}", relativePath, trashRelative);
        return trashRelative;
    }

    // ── Restore ───────────────────────────────────────────────
    public async Task RestoreFromTrashAsync(string trashRelativePath,
                                            string originalRelativePath,
                                            CancellationToken ct = default)
    {
        var trashAbs    = Resolve(trashRelativePath);
        var originalAbs = Resolve(originalRelativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(originalAbs)!);
        await Task.Run(() => File.Move(trashAbs, originalAbs), ct);

        // Remove placeholder
        var placeholder = originalAbs + ".photovault-deleted";
        if (File.Exists(placeholder)) File.Delete(placeholder);

        _log.LogInformation("Restored {Trash} → {Path}", trashRelativePath, originalRelativePath);
    }

    // ── Stream ────────────────────────────────────────────────
    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default)
        => Task.FromResult<Stream>(File.OpenRead(Resolve(relativePath)));

    // ── Enumerate (first-boot scan) ───────────────────────────
    public async IAsyncEnumerable<string> EnumerateMediaFilesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var photosRoot = Resolve("PhotosVideos");
        foreach (var file in Directory.EnumerateFiles(photosRoot, "*.*", SearchOption.AllDirectories))
        {
            if (ct.IsCancellationRequested) yield break;
            if (!SupportedExtensions.Contains(Path.GetExtension(file))) continue;
            // Skip placeholder files
            if (file.EndsWith(".photovault-deleted", StringComparison.OrdinalIgnoreCase)) continue;
            yield return Path.GetRelativePath(_opts.MediaRoot, file);
            await Task.Yield();
        }
    }
}

public class MediaRootOptions
{
    public const string Section = "MediaRoot";
    public string MediaRoot { get; set; } = default!;
}
