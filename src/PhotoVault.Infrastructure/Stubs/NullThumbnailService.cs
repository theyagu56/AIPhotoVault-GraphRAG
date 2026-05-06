using PhotoVault.Core.Domain;
using PhotoVault.Core.Interfaces;

namespace PhotoVault.Infrastructure.Stubs;

/// <summary>
/// Phase 1 stub — thumbnail generation not yet implemented.
/// Replaced by ImageSharpThumbnailService in Phase 4.
/// </summary>
public sealed class NullThumbnailService : IThumbnailService
{
    public Task<Thumbnail> GenerateAsync(string mediaId, string absoluteSourcePath,
        ThumbnailSize size, CancellationToken ct = default)
        => Task.FromResult(new Thumbnail(Guid.NewGuid().ToString(), mediaId, size, "", 0, 0, DateTime.UtcNow));

    public Task<IReadOnlyList<Thumbnail>> GenerateAllSizesAsync(string mediaId,
        string absoluteSourcePath, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Thumbnail>>(Array.Empty<Thumbnail>());

    public string GetThumbnailPath(string mediaId, ThumbnailSize size) => "";

    public Task<bool> ExistsAsync(string mediaId, ThumbnailSize size,
        CancellationToken ct = default)
        => Task.FromResult(false);
}
