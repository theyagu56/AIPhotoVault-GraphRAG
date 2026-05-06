using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using PhotoVault.Core.Domain;
using PhotoVault.Core.Interfaces;
using PhotoVault.Infrastructure.FileSystem;

namespace PhotoVault.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly string _connStr;

    public UserRepository(IOptions<MediaRootOptions> opts)
    {
        var dbDir  = Path.Combine(opts.Value.MediaRoot, "Application", "Database");
        Directory.CreateDirectory(dbDir);
        var dbPath = Path.Combine(dbDir, "photovault.db");
        _connStr = $"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared;";
    }

    private SqliteConnection Conn() => new(_connStr);

    public async Task<User?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await using var db = Conn();
        return await db.QuerySingleOrDefaultAsync<User>("SELECT * FROM Users WHERE Id=@id", new { id });
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        await using var db = Conn();
        return await db.QuerySingleOrDefaultAsync<User>(
            "SELECT * FROM Users WHERE Email=@email COLLATE NOCASE", new { email });
    }

    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = Conn();
        return (await db.QueryAsync<User>("SELECT * FROM Users ORDER BY CreatedAt")).AsList();
    }

    public async Task<IReadOnlyList<User>> GetPendingAsync(CancellationToken ct = default)
    {
        await using var db = Conn();
        return (await db.QueryAsync<User>(
            "SELECT * FROM Users WHERE Role='Pending' ORDER BY CreatedAt")).AsList();
    }

    public async Task<string> UpsertAsync(User u, CancellationToken ct = default)
    {
        await using var db = Conn();
        await db.ExecuteAsync(@"
            INSERT INTO Users (Id,Email,DisplayName,PhotoUrl,Role,CreatedAt)
            VALUES (@Id,@Email,@DisplayName,@PhotoUrl,@Role,@CreatedAt)
            ON CONFLICT(Id) DO UPDATE SET
                DisplayName=excluded.DisplayName,
                PhotoUrl=excluded.PhotoUrl,
                LastLoginAt=@CreatedAt", u);
        return u.Id;
    }

    public async Task ApproveAsync(string id, string adminId, CancellationToken ct = default)
    {
        await using var db = Conn();
        await db.ExecuteAsync(
            "UPDATE Users SET Role='User', ApprovedAt=@now, ApprovedBy=@adminId WHERE Id=@id",
            new { id, now = DateTime.UtcNow, adminId });
    }

    public async Task RejectAsync(string id, string adminId, CancellationToken ct = default)
    {
        await using var db = Conn();
        await db.ExecuteAsync(
            "UPDATE Users SET Role='Rejected', ApprovedAt=@now, ApprovedBy=@adminId WHERE Id=@id",
            new { id, now = DateTime.UtcNow, adminId });
    }

    public async Task UpdateLastLoginAsync(string id, CancellationToken ct = default)
    {
        await using var db = Conn();
        await db.ExecuteAsync(
            "UPDATE Users SET LastLoginAt=@now WHERE Id=@id", new { id, now = DateTime.UtcNow });
    }

    public async Task<bool> IsAdminAsync(string id, CancellationToken ct = default)
    {
        await using var db = Conn();
        return await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM Users WHERE Id=@id AND Role='Admin' AND IsActive=1", new { id }) > 0;
    }

    public async Task<bool> HasAnyAdminAsync(CancellationToken ct = default)
    {
        await using var db = Conn();
        return await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM Users WHERE Role='Admin'") > 0;
    }
}
