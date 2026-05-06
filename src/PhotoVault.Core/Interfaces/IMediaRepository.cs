using PhotoVault.Core.Domain;

namespace PhotoVault.Core.Interfaces;

public interface IMediaRepository
{
    Task<Media?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<Media?> GetByHashAsync(string hash, CancellationToken ct = default);
    Task<Media?> GetByPathAsync(string relativePath, CancellationToken ct = default);
    Task<IReadOnlyList<Media>> GetPagedAsync(MediaQuery query, CancellationToken ct = default);
    Task<int> CountAsync(MediaQuery query, CancellationToken ct = default);
    Task<string> InsertAsync(Media media, CancellationToken ct = default);
    Task UpdateAsync(Media media, CancellationToken ct = default);
    Task<bool> ExistsByHashAsync(string hash, CancellationToken ct = default);
    Task<IReadOnlyList<Media>> GetUnprocessedAsync(int batchSize, CancellationToken ct = default);
    Task MarkAIProcessedAsync(string id, string modelUsed, CancellationToken ct = default);
    Task MoveToTrashAsync(string id, string userId, string trashPath, CancellationToken ct = default);
    Task RestoreFromTrashAsync(string id, CancellationToken ct = default);

    // ── AI feature queries ────────────────────────────────────
    Task<IReadOnlyList<Media>> GetBlurryAsync(int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<Media>> GetDuplicatesAsync(int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetAllPerceptualHashesAsync(CancellationToken ct = default);
    Task UpdateAIResultsAsync(string id, double blurScore, bool isBlurry,
                              string? pHash, bool isDuplicate, string? duplicateOfId,
                              CancellationToken ct = default);
    Task<IReadOnlyList<Media>> GetUnprocessedBatchAsync(int batchSize, CancellationToken ct = default);
}

public record MediaQuery(
    int Page = 1,
    int PageSize = 50,
    string? SearchText = null,
    string? TagName = null,
    string? AlbumId = null,
    MediaType? MediaType = null,
    bool InTrash = false,
    bool OnlyFavorites = false,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    string? SortBy = "CapturedAt",
    bool Descending = true
);
