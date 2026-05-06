using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using PhotoVault.Core.Domain;
using PhotoVault.Core.Interfaces;
using PhotoVault.Infrastructure.FileSystem;

namespace PhotoVault.Infrastructure.Repositories;

public class TagRepository : ITagRepository
{
    private readonly string _connStr;

    public TagRepository(IOptions<MediaRootOptions> opts)
    {
        var dbDir  = Path.Combine(opts.Value.MediaRoot, "Application", "Database");
        Directory.CreateDirectory(dbDir);
        _connStr = $"Data Source={Path.Combine(dbDir, "photovault.db")};Mode=ReadWriteCreate;Cache=Shared;";
    }

    private SqliteConnection Conn() => new(_connStr);

    // ── Upsert tag (get or create) ─────────────────────────────
    public async Task<Tag> UpsertTagAsync(string name, TagCategory category, bool isAI,
                                           CancellationToken ct = default)
    {
        await using var conn = Conn();
        await conn.OpenAsync(ct);

        // Try to find existing
        var existing = await conn.QueryFirstOrDefaultAsync<Tag>(
            "SELECT Id, Name, Category, IsAIGenerated, CreatedAt FROM Tags WHERE Name = @Name COLLATE NOCASE",
            new { Name = name });

        if (existing is not null) return existing;

        // Insert new
        var tag = new Tag
        {
            Id            = Guid.NewGuid().ToString(),
            Name          = name.ToLowerInvariant().Trim(),
            Category      = category,
            IsAIGenerated = isAI,
            CreatedAt     = DateTime.UtcNow
        };

        await conn.ExecuteAsync(
            @"INSERT OR IGNORE INTO Tags (Id, Name, Category, IsAIGenerated, CreatedAt)
              VALUES (@Id, @Name, @Category, @IsAIGenerated, @CreatedAt)",
            new {
                tag.Id,
                tag.Name,
                Category      = category.ToString(),
                IsAIGenerated = isAI ? 1 : 0,
                CreatedAt     = tag.CreatedAt.ToString("o")
            });

        return tag;
    }

    // ── Attach tag to media ────────────────────────────────────
    public async Task AddMediaTagAsync(string mediaId, string tagId, double confidence,
                                        TagSource source, CancellationToken ct = default)
    {
        await using var conn = Conn();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(
            @"INSERT OR REPLACE INTO MediaTags (MediaId, TagId, Confidence, Source, AddedAt)
              VALUES (@MediaId, @TagId, @Confidence, @Source, @AddedAt)",
            new {
                MediaId    = mediaId,
                TagId      = tagId,
                Confidence = confidence,
                Source     = source.ToString(),
                AddedAt    = DateTime.UtcNow.ToString("o")
            });
    }

    // ── Tags for a media item ─────────────────────────────────
    public async Task<IReadOnlyList<Tag>> GetTagsForMediaAsync(string mediaId,
                                                                CancellationToken ct = default)
    {
        await using var conn = Conn();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<dynamic>(
            @"SELECT t.Id, t.Name, t.Category, t.IsAIGenerated, t.CreatedAt,
                     mt.Confidence, mt.Source
              FROM Tags t
              JOIN MediaTags mt ON mt.TagId = t.Id
              WHERE mt.MediaId = @MediaId
              ORDER BY mt.Confidence DESC",
            new { MediaId = mediaId });

        return rows.Select(r => new Tag
        {
            Id            = r.Id,
            Name          = r.Name,
            Category      = Enum.TryParse<TagCategory>((string)r.Category, out var cat) ? cat : TagCategory.General,
            IsAIGenerated = (long)r.IsAIGenerated == 1,
            CreatedAt     = DateTime.Parse((string)r.CreatedAt),
            Confidence    = r.Confidence,
            Source        = Enum.TryParse<TagSource>((string)r.Source, out var src) ? src : TagSource.AI
        }).ToList();
    }

    // ── Save / update caption ─────────────────────────────────
    public async Task SaveCaptionAsync(string mediaId, string caption, string modelUsed,
                                        CancellationToken ct = default)
    {
        await using var conn = Conn();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(
            @"INSERT INTO MediaCaptions (Id, MediaId, Caption, ModelUsed, CreatedAt)
              VALUES (@Id, @MediaId, @Caption, @ModelUsed, @CreatedAt)
              ON CONFLICT(MediaId) DO UPDATE SET
                Caption   = excluded.Caption,
                ModelUsed = excluded.ModelUsed",
            new {
                Id        = Guid.NewGuid().ToString(),
                MediaId   = mediaId,
                Caption   = caption,
                ModelUsed = modelUsed,
                CreatedAt = DateTime.UtcNow.ToString("o")
            });
    }

    // ── Get caption ───────────────────────────────────────────
    public async Task<string?> GetCaptionAsync(string mediaId, CancellationToken ct = default)
    {
        await using var conn = Conn();
        await conn.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<string>(
            "SELECT Caption FROM MediaCaptions WHERE MediaId = @MediaId", new { MediaId = mediaId });
    }
}
