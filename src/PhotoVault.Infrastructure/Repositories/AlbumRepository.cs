using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using PhotoVault.Core.Domain;
using PhotoVault.Core.Interfaces;
using PhotoVault.Infrastructure.FileSystem;

namespace PhotoVault.Infrastructure.Repositories;

public class AlbumRepository : IAlbumRepository
{
    private readonly string _connStr;
    public AlbumRepository(IOptions<MediaRootOptions> opts)
    {
        var dbDir  = Path.Combine(opts.Value.MediaRoot, "Application", "Database");
        Directory.CreateDirectory(dbDir);
        var dbPath = Path.Combine(dbDir, "photovault.db");
        _connStr = $"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared;";
    }
    private SqliteConnection Conn() => new(_connStr);

    public async Task<Album?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await using var db = Conn();
        return await db.QuerySingleOrDefaultAsync<Album>("SELECT * FROM Albums WHERE Id=@id", new { id });
    }

    public async Task<IReadOnlyList<Album>> GetByUserAsync(string? userId, CancellationToken ct = default)
    {
        await using var db = Conn();
        // null userId → return all albums (used by AutoAlbumService and list-all endpoint)
        if (userId is null)
            return (await db.QueryAsync<Album>("SELECT * FROM Albums ORDER BY UpdatedAt DESC")).AsList();

        return (await db.QueryAsync<Album>(
            @"SELECT * FROM Albums
              WHERE CreatedByUserId=@userId OR CreatedByUserId='system'
              ORDER BY UpdatedAt DESC", new { userId })).AsList();
    }

    public async Task<string> CreateAsync(Album a, CancellationToken ct = default)
    {
        await using var db = Conn();
        await db.ExecuteAsync(@"
            INSERT INTO Albums (Id,Name,Description,CoverMediaId,AlbumType,CreatedByUserId,CreatedAt,UpdatedAt)
            VALUES (@Id,@Name,@Description,@CoverMediaId,@AlbumType,@CreatedByUserId,@CreatedAt,@UpdatedAt)",
            new {
                a.Id, a.Name, a.Description, a.CoverMediaId,
                AlbumType       = a.AlbumType.ToString(),   // enum → string for SQLite CHECK
                a.CreatedByUserId,
                CreatedAt = a.CreatedAt.ToString("O"),
                UpdatedAt = a.UpdatedAt.ToString("O")
            });
        return a.Id;
    }

    public async Task UpdateAsync(Album a, CancellationToken ct = default)
    {
        await using var db = Conn();
        await db.ExecuteAsync("UPDATE Albums SET Name=@Name,Description=@Description,CoverMediaId=@CoverMediaId WHERE Id=@Id", a);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var db = Conn();
        await db.ExecuteAsync("DELETE FROM Albums WHERE Id=@id", new { id });
    }

    public async Task AddMediaAsync(string albumId, string mediaId, CancellationToken ct = default)
    {
        await using var db = Conn();
        await db.ExecuteAsync(
            "INSERT OR IGNORE INTO AlbumMedia (AlbumId,MediaId,AddedAt) VALUES (@albumId,@mediaId,@now)",
            new { albumId, mediaId, now = DateTime.UtcNow });
    }

    public async Task RemoveMediaAsync(string albumId, string mediaId, CancellationToken ct = default)
    {
        await using var db = Conn();
        await db.ExecuteAsync("DELETE FROM AlbumMedia WHERE AlbumId=@albumId AND MediaId=@mediaId", new { albumId, mediaId });
    }

    public async Task<IReadOnlyList<Album>> GetByMediaIdAsync(string mediaId, CancellationToken ct = default)
    {
        await using var db = Conn();
        return (await db.QueryAsync<Album>(@"
            SELECT a.* FROM Albums a
            JOIN AlbumMedia am ON am.AlbumId = a.Id
            WHERE am.MediaId = @mediaId
            ORDER BY a.UpdatedAt DESC", new { mediaId })).AsList();
    }
}
