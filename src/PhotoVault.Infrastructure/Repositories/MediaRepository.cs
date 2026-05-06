using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using PhotoVault.Core.Domain;
using PhotoVault.Core.Interfaces;
using PhotoVault.Infrastructure.FileSystem;

namespace PhotoVault.Infrastructure.Repositories;

public class MediaRepository : IMediaRepository
{
    private readonly string _connStr;

    public MediaRepository(IOptions<MediaRootOptions> opts)
    {
        var dbDir  = Path.Combine(opts.Value.MediaRoot, "Application", "Database");
        Directory.CreateDirectory(dbDir);
        var dbPath = Path.Combine(dbDir, "photovault.db");
        _connStr = $"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared;";
        SqlMapper.AddTypeHandler(new DateTimeHandler());
    }

    private SqliteConnection Conn() => new(_connStr);

    // ── Fetch single ──────────────────────────────────────────
    public async Task<Media?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await using var db = Conn();
        return await db.QuerySingleOrDefaultAsync<Media>(
            "SELECT * FROM Media WHERE Id = @id", new { id });
    }

    public async Task<Media?> GetByHashAsync(string hash, CancellationToken ct = default)
    {
        await using var db = Conn();
        return await db.QuerySingleOrDefaultAsync<Media>(
            "SELECT * FROM Media WHERE FileHash = @hash AND InTrash = 0", new { hash });
    }

    public async Task<Media?> GetByPathAsync(string relativePath, CancellationToken ct = default)
    {
        await using var db = Conn();
        return await db.QuerySingleOrDefaultAsync<Media>(
            "SELECT * FROM Media WHERE OriginalPath = @relativePath", new { relativePath });
    }

    // ── Paged list ────────────────────────────────────────────
    public async Task<IReadOnlyList<Media>> GetPagedAsync(MediaQuery query, CancellationToken ct = default)
    {
        await using var db = Conn();
        var (sql, p) = BuildQuery(query, select: "SELECT m.*");
        sql += $" ORDER BY m.{query.SortBy} {(query.Descending ? "DESC" : "ASC")}";
        sql += $" LIMIT {query.PageSize} OFFSET {(query.Page - 1) * query.PageSize}";
        return (await db.QueryAsync<Media>(sql, p)).AsList();
    }

    public async Task<int> CountAsync(MediaQuery query, CancellationToken ct = default)
    {
        await using var db = Conn();
        var (sql, p) = BuildQuery(query, select: "SELECT COUNT(*)");
        return await db.ExecuteScalarAsync<int>(sql, p);
    }

    private static (string sql, DynamicParameters p) BuildQuery(MediaQuery q, string select)
    {
        var sb = new StringBuilder($"{select} FROM Media m");
        var p  = new DynamicParameters();
        var where = new List<string> { "1=1" };

        if (!string.IsNullOrWhiteSpace(q.TagName))
        {
            sb.Append(" JOIN MediaTags mt ON mt.MediaId = m.Id JOIN Tags t ON t.Id = mt.TagId");
            where.Add("t.Name = @tagName COLLATE NOCASE");
            p.Add("tagName", q.TagName);
        }
        if (!string.IsNullOrWhiteSpace(q.AlbumId))
        {
            sb.Append(" JOIN AlbumMedia am ON am.MediaId = m.Id");
            where.Add("am.AlbumId = @albumId");
            p.Add("albumId", q.AlbumId);
        }

        where.Add($"m.InTrash = {(q.InTrash ? 1 : 0)}");
        if (q.MediaType.HasValue) { where.Add("m.MediaType = @mtype"); p.Add("mtype", q.MediaType.Value.ToString()); }
        if (q.FromDate.HasValue)  { where.Add("m.CapturedAt >= @from"); p.Add("from", q.FromDate.Value); }
        if (q.ToDate.HasValue)    { where.Add("m.CapturedAt <= @to");   p.Add("to",   q.ToDate.Value); }

        if (!string.IsNullOrWhiteSpace(q.SearchText))
        {
            where.Add("(m.FileName LIKE @search OR EXISTS (SELECT 1 FROM MediaTags mt2 JOIN Tags t2 ON t2.Id=mt2.TagId WHERE mt2.MediaId=m.Id AND t2.Name LIKE @search))");
            p.Add("search", $"%{q.SearchText}%");
        }

        sb.Append(" WHERE "); sb.Append(string.Join(" AND ", where));
        return (sb.ToString(), p);
    }

    // ── Mutations ─────────────────────────────────────────────
    public async Task<string> InsertAsync(Media m, CancellationToken ct = default)
    {
        await using var db = Conn();
        await db.ExecuteAsync(@"
            INSERT INTO Media (Id,FileName,OriginalPath,FileHash,PerceptualHash,MediaType,MimeType,
                FileSizeBytes,Width,Height,DurationSeconds,CapturedAt,Latitude,Longitude,
                CameraModel,IsBlurry,BlurScore,IsDuplicate,DuplicateOfId,AIProcessed,CreatedAt,UpdatedAt)
            VALUES (@Id,@FileName,@OriginalPath,@FileHash,@PerceptualHash,@MediaType,@MimeType,
                @FileSizeBytes,@Width,@Height,@DurationSeconds,@CapturedAt,@Latitude,@Longitude,
                @CameraModel,@IsBlurry,@BlurScore,@IsDuplicate,@DuplicateOfId,@AIProcessed,@CreatedAt,@UpdatedAt)", ToDbParams(m));
        return m.Id;
    }

    public async Task UpdateAsync(Media m, CancellationToken ct = default)
    {
        await using var db = Conn();
        await db.ExecuteAsync(@"
            UPDATE Media SET FileName=@FileName,OriginalPath=@OriginalPath,FileHash=@FileHash,
                MediaType=@MediaType,MimeType=@MimeType,FileSizeBytes=@FileSizeBytes,
                Width=@Width,Height=@Height,DurationSeconds=@DurationSeconds,
                CapturedAt=@CapturedAt,Latitude=@Latitude,Longitude=@Longitude,
                CameraModel=@CameraModel,IsBlurry=@IsBlurry,BlurScore=@BlurScore,
                IsDuplicate=@IsDuplicate,DuplicateOfId=@DuplicateOfId
            WHERE Id=@Id", ToDbParams(m));
    }

    private static object ToDbParams(Media m) => new
    {
        m.Id,
        m.FileName,
        m.OriginalPath,
        m.FileHash,
        m.PerceptualHash,
        MediaType = m.MediaType.ToString(),
        m.MimeType,
        m.FileSizeBytes,
        m.Width,
        m.Height,
        m.DurationSeconds,
        m.CapturedAt,
        m.Latitude,
        m.Longitude,
        m.CameraModel,
        m.IsBlurry,
        m.BlurScore,
        m.IsDuplicate,
        m.DuplicateOfId,
        m.AIProcessed,
        m.CreatedAt,
        m.UpdatedAt
    };

    public async Task<bool> ExistsByHashAsync(string hash, CancellationToken ct = default)
    {
        await using var db = Conn();
        return await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM Media WHERE FileHash=@hash AND InTrash=0", new { hash }) > 0;
    }

    public async Task<IReadOnlyList<Media>> GetUnprocessedAsync(int batchSize, CancellationToken ct = default)
    {
        await using var db = Conn();
        return (await db.QueryAsync<Media>(
            "SELECT * FROM Media WHERE AIProcessed=0 AND InTrash=0 ORDER BY CreatedAt LIMIT @batchSize",
            new { batchSize })).AsList();
    }

    public async Task MarkAIProcessedAsync(string id, string modelUsed, CancellationToken ct = default)
    {
        await using var db = Conn();
        await db.ExecuteAsync(
            "UPDATE Media SET AIProcessed=1, AIProcessedAt=@now, AIModelUsed=@modelUsed WHERE Id=@id",
            new { id, now = DateTime.UtcNow, modelUsed });
    }

    public async Task MoveToTrashAsync(string id, string userId, string trashPath, CancellationToken ct = default)
    {
        await using var db = Conn();
        await db.ExecuteAsync(@"
            UPDATE Media SET InTrash=1, TrashedAt=@now, TrashedByUserId=@userId, TrashPath=@trashPath
            WHERE Id=@id", new { id, now = DateTime.UtcNow, userId, trashPath });
    }

    public async Task RestoreFromTrashAsync(string id, CancellationToken ct = default)
    {
        await using var db = Conn();
        await db.ExecuteAsync(
            "UPDATE Media SET InTrash=0, TrashedAt=NULL, TrashedByUserId=NULL, TrashPath=NULL, RestoredAt=@now WHERE Id=@id",
            new { id, now = DateTime.UtcNow });
    }

    // ── AI feature queries ────────────────────────────────────
    public async Task<IReadOnlyList<Media>> GetBlurryAsync(int page, int pageSize,
                                                            CancellationToken ct = default)
    {
        await using var db = Conn();
        return (await db.QueryAsync<Media>(
            @"SELECT * FROM Media WHERE IsBlurry=1 AND InTrash=0
              ORDER BY BlurScore ASC
              LIMIT @pageSize OFFSET @offset",
            new { pageSize, offset = (page - 1) * pageSize })).AsList();
    }

    public async Task<IReadOnlyList<Media>> GetDuplicatesAsync(int page, int pageSize,
                                                                CancellationToken ct = default)
    {
        await using var db = Conn();
        return (await db.QueryAsync<Media>(
            @"SELECT * FROM Media WHERE IsDuplicate=1 AND InTrash=0
              ORDER BY CapturedAt DESC
              LIMIT @pageSize OFFSET @offset",
            new { pageSize, offset = (page - 1) * pageSize })).AsList();
    }

    public async Task<IReadOnlyList<string>> GetAllPerceptualHashesAsync(CancellationToken ct = default)
    {
        await using var db = Conn();
        var rows = await db.QueryAsync<(string Id, string Hash)>(
            "SELECT Id, PerceptualHash FROM Media WHERE PerceptualHash IS NOT NULL AND InTrash=0");
        return rows.Select(r => $"{r.Id}:{r.Hash}").ToList();
    }

    public async Task UpdateAIResultsAsync(string id, double blurScore, bool isBlurry,
                                            string? pHash, bool isDuplicate, string? duplicateOfId,
                                            CancellationToken ct = default)
    {
        await using var db = Conn();
        await db.ExecuteAsync(
            @"UPDATE Media SET
                BlurScore=@blurScore, IsBlurry=@isBlurry,
                PerceptualHash=@pHash,
                IsDuplicate=@isDuplicate, DuplicateOfId=@duplicateOfId
              WHERE Id=@id",
            new { id, blurScore, isBlurry = isBlurry ? 1 : 0,
                  pHash, isDuplicate = isDuplicate ? 1 : 0, duplicateOfId });
    }

    public async Task<IReadOnlyList<Media>> GetUnprocessedBatchAsync(int batchSize,
                                                                      CancellationToken ct = default)
    {
        await using var db = Conn();
        return (await db.QueryAsync<Media>(
            "SELECT * FROM Media WHERE AIProcessed=0 AND InTrash=0 ORDER BY CreatedAt LIMIT @batchSize",
            new { batchSize })).AsList();
    }
}

// Dapper type handler: SQLite TEXT ↔ DateTime UTC
file sealed class DateTimeHandler : SqlMapper.TypeHandler<DateTime>
{
    public override void SetValue(System.Data.IDbDataParameter parameter, DateTime value)
        => parameter.Value = value.ToString("O");
    public override DateTime Parse(object value)
        => DateTime.Parse((string)value, null, System.Globalization.DateTimeStyles.RoundtripKind);
}
