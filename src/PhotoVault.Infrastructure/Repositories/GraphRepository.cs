using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoVault.Core.Domain;
using PhotoVault.Core.Interfaces;
using PhotoVault.Infrastructure.FileSystem;

namespace PhotoVault.Infrastructure.Repositories;

/// <summary>
/// SQLite-backed graph store.
/// Tables are created on first use — safe to call on existing databases.
/// </summary>
public sealed class GraphRepository : IGraphRepository
{
    private readonly string _connStr;
    private readonly ILogger<GraphRepository> _log;

    public GraphRepository(IOptions<MediaRootOptions> opts, ILogger<GraphRepository> log)
    {
        _log = log;
        var dbDir  = Path.Combine(opts.Value.MediaRoot, "Application", "Database");
        Directory.CreateDirectory(dbDir);
        var dbPath = Path.Combine(dbDir, "photovault.db");
        _connStr   = $"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared;";
        EnsureTablesAsync().GetAwaiter().GetResult();
    }

    private SqliteConnection Conn() => new(_connStr);

    // ── Bootstrap ─────────────────────────────────────────────
    private async Task EnsureTablesAsync()
    {
        await using var db = Conn();
        await db.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS GraphNodes (
                Id        TEXT PRIMARY KEY,
                NodeType  TEXT NOT NULL,
                Label     TEXT NOT NULL,
                Metadata  TEXT,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now','utc')),
                UpdatedAt TEXT NOT NULL DEFAULT (datetime('now','utc'))
            );
            CREATE INDEX IF NOT EXISTS idx_gnodes_type  ON GraphNodes(NodeType);
            CREATE INDEX IF NOT EXISTS idx_gnodes_label ON GraphNodes(Label);

            CREATE TABLE IF NOT EXISTS GraphEdges (
                FromId    TEXT NOT NULL REFERENCES GraphNodes(Id) ON DELETE CASCADE,
                ToId      TEXT NOT NULL REFERENCES GraphNodes(Id) ON DELETE CASCADE,
                EdgeType  TEXT NOT NULL,
                Weight    REAL NOT NULL DEFAULT 1.0,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now','utc')),
                PRIMARY KEY (FromId, ToId, EdgeType)
            );
            CREATE INDEX IF NOT EXISTS idx_gedges_from ON GraphEdges(FromId);
            CREATE INDEX IF NOT EXISTS idx_gedges_to   ON GraphEdges(ToId);
            CREATE INDEX IF NOT EXISTS idx_gedges_type ON GraphEdges(EdgeType);
        ");
    }

    // ── Node operations ───────────────────────────────────────
    public async Task UpsertNodeAsync(GraphNode node, CancellationToken ct = default)
    {
        await using var db = Conn();
        await db.ExecuteAsync(@"
            INSERT INTO GraphNodes (Id, NodeType, Label, Metadata, CreatedAt, UpdatedAt)
            VALUES (@Id, @NodeType, @Label, @Metadata, @CreatedAt, @UpdatedAt)
            ON CONFLICT(Id) DO UPDATE SET
                Label     = excluded.Label,
                Metadata  = excluded.Metadata,
                UpdatedAt = excluded.UpdatedAt",
            new { node.Id, node.NodeType, node.Label, node.Metadata,
                  CreatedAt = node.CreatedAt.ToString("O"),
                  UpdatedAt = DateTime.UtcNow.ToString("O") });
    }

    public async Task<GraphNode?> GetNodeAsync(string id, CancellationToken ct = default)
    {
        await using var db = Conn();
        return await db.QuerySingleOrDefaultAsync<GraphNode>(
            "SELECT * FROM GraphNodes WHERE Id = @id", new { id });
    }

    public async Task DeletePhotoNodesAsync(string photoId, CancellationToken ct = default)
    {
        // Remove the Photo node; cascade deletes its edges
        await using var db = Conn();
        await db.ExecuteAsync(
            "DELETE FROM GraphNodes WHERE Id = @id", new { id = $"photo:{photoId}" });
    }

    // ── Edge operations ───────────────────────────────────────
    public async Task UpsertEdgeAsync(GraphEdge edge, CancellationToken ct = default)
    {
        await using var db = Conn();
        await db.ExecuteAsync(@"
            INSERT INTO GraphEdges (FromId, ToId, EdgeType, Weight, CreatedAt)
            VALUES (@FromId, @ToId, @EdgeType, @Weight, @CreatedAt)
            ON CONFLICT(FromId, ToId, EdgeType) DO UPDATE SET
                Weight = excluded.Weight",
            new { edge.FromId, edge.ToId, edge.EdgeType, edge.Weight,
                  CreatedAt = edge.CreatedAt.ToString("O") });
    }

    public async Task IncrementEdgeWeightAsync(string fromId, string toId, string edgeType,
                                                double delta = 1.0,
                                                CancellationToken ct = default)
    {
        await using var db = Conn();
        await db.ExecuteAsync(@"
            INSERT INTO GraphEdges (FromId, ToId, EdgeType, Weight, CreatedAt)
            VALUES (@fromId, @toId, @edgeType, @delta, @now)
            ON CONFLICT(FromId, ToId, EdgeType) DO UPDATE SET
                Weight = Weight + @delta",
            new { fromId, toId, edgeType, delta, now = DateTime.UtcNow.ToString("O") });
    }

    // ── Traversal ─────────────────────────────────────────────
    public async Task<IReadOnlyList<GraphEdge>> GetEdgesFromAsync(string nodeId,
                                                                    string? edgeType = null,
                                                                    CancellationToken ct = default)
    {
        await using var db = Conn();
        var sql = edgeType is null
            ? "SELECT * FROM GraphEdges WHERE FromId = @nodeId ORDER BY Weight DESC"
            : "SELECT * FROM GraphEdges WHERE FromId = @nodeId AND EdgeType = @edgeType ORDER BY Weight DESC";
        return (await db.QueryAsync<GraphEdge>(sql, new { nodeId, edgeType })).AsList();
    }

    public async Task<IReadOnlyList<GraphEdge>> GetEdgesToAsync(string nodeId,
                                                                  string? edgeType = null,
                                                                  CancellationToken ct = default)
    {
        await using var db = Conn();
        var sql = edgeType is null
            ? "SELECT * FROM GraphEdges WHERE ToId = @nodeId ORDER BY Weight DESC"
            : "SELECT * FROM GraphEdges WHERE ToId = @nodeId AND EdgeType = @edgeType ORDER BY Weight DESC";
        return (await db.QueryAsync<GraphEdge>(sql, new { nodeId, edgeType })).AsList();
    }

    /// <summary>BFS over GraphEdges — returns all reachable node IDs within maxHops.</summary>
    public async Task<IReadOnlyList<string>> ExpandAsync(IEnumerable<string> seedIds,
                                                          string edgeType,
                                                          int maxHops = 2,
                                                          CancellationToken ct = default)
    {
        await using var db = Conn();
        var visited  = new HashSet<string>(seedIds);
        var frontier = new Queue<string>(seedIds);
        var result   = new List<string>();

        for (int hop = 0; hop < maxHops && frontier.Count > 0; hop++)
        {
            var nextFrontier = new List<string>();
            while (frontier.Count > 0)
            {
                var current = frontier.Dequeue();
                var edges   = await db.QueryAsync<GraphEdge>(
                    "SELECT * FROM GraphEdges WHERE FromId = @current AND EdgeType = @edgeType ORDER BY Weight DESC",
                    new { current, edgeType });

                foreach (var e in edges)
                {
                    if (visited.Add(e.ToId))
                    {
                        result.Add(e.ToId);
                        nextFrontier.Add(e.ToId);
                    }
                }
            }
            foreach (var n in nextFrontier) frontier.Enqueue(n);
        }
        return result;
    }

    public async Task<IReadOnlyList<string>> GetPhotosByTagNodeAsync(string tagNodeId,
                                                                       CancellationToken ct = default)
    {
        await using var db = Conn();
        // Photos → hasTag → Tag; traverse backwards
        var rows = await db.QueryAsync<string>(
            @"SELECT FromId FROM GraphEdges
              WHERE ToId = @tagNodeId AND EdgeType = 'hasTag'
              ORDER BY Weight DESC", new { tagNodeId });
        return rows.Select(r => r.Replace("photo:", "")).ToList();
    }

    // ── Stats ─────────────────────────────────────────────────
    public async Task<int> CountNodesAsync(string? nodeType = null, CancellationToken ct = default)
    {
        await using var db = Conn();
        return nodeType is null
            ? await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM GraphNodes")
            : await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM GraphNodes WHERE NodeType=@nodeType",
                                               new { nodeType });
    }

    public async Task<int> CountEdgesAsync(string? edgeType = null, CancellationToken ct = default)
    {
        await using var db = Conn();
        return edgeType is null
            ? await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM GraphEdges")
            : await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM GraphEdges WHERE EdgeType=@edgeType",
                                               new { edgeType });
    }
}
