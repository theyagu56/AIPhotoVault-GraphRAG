using PhotoVault.Core.Domain;

namespace PhotoVault.Core.Interfaces;

public interface IThumbnailService
{
    Task<Thumbnail> GenerateAsync(string mediaId, string absoluteSourcePath,
                                  ThumbnailSize size, CancellationToken ct = default);
    Task<IReadOnlyList<Thumbnail>> GenerateAllSizesAsync(string mediaId,
                                  string absoluteSourcePath, CancellationToken ct = default);
    string GetThumbnailPath(string mediaId, ThumbnailSize size);
    Task<bool> ExistsAsync(string mediaId, ThumbnailSize size, CancellationToken ct = default);
}
