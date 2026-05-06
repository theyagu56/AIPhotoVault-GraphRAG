using PhotoVault.Core.Domain;

namespace PhotoVault.Core.Interfaces;

public interface IAlbumRepository
{
    Task<Album?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<Album>> GetByUserAsync(string? userId, CancellationToken ct = default);
    Task<string> CreateAsync(Album album, CancellationToken ct = default);
    Task UpdateAsync(Album album, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task AddMediaAsync(string albumId, string mediaId, CancellationToken ct = default);
    Task RemoveMediaAsync(string albumId, string mediaId, CancellationToken ct = default);
    Task<IReadOnlyList<Album>> GetByMediaIdAsync(string mediaId, CancellationToken ct = default);
}
