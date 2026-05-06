namespace PhotoVault.Core.Interfaces;

/// <summary>
/// Storage abstraction — swap local → Azure/S3 without changing business logic.
/// </summary>
public interface IFileStorageService
{
    /// Compute SHA-256 hash of a file
    Task<string> ComputeHashAsync(string absolutePath, CancellationToken ct = default);

    /// Copy file into /PhotosVideos/ returning its relative path
    Task<string> IngestFileAsync(string sourcePath, string? subFolder = null, CancellationToken ct = default);

    /// Move file to /Application/Trash/ and return new relative path
    Task<string> MoveToTrashAsync(string relativePath, CancellationToken ct = default);

    /// Restore file from /Application/Trash/ back to original relative path
    Task RestoreFromTrashAsync(string trashRelativePath, string originalRelativePath, CancellationToken ct = default);

    /// Resolve relative path to absolute
    string Resolve(string relativePath);

    /// Open a stream for reading (works for local + future cloud)
    Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default);

    /// List all media files under /PhotosVideos/ (for first-boot scan)
    IAsyncEnumerable<string> EnumerateMediaFilesAsync(CancellationToken ct = default);
}
