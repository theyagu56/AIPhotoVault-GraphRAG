using PhotoVault.Core.Domain;

namespace PhotoVault.Core.Interfaces;

public interface IGraphRepository
{
    // ── Node operations ───────────────────────────────────────
    Task UpsertNodeAsync(GraphNode node, CancellationToken ct = default);

    Task<GraphNode?> GetNodeAsync(string id, CancellationToken ct = default);

    /// <summary>Remove all nodes (and their edges) for a given photo.</summary>
    Task DeletePhotoNodesAsync(string photoId, CancellationToken ct = default);

    // ── Edge operations ───────────────────────────────────────
    Task UpsertEdgeAsync(GraphEdge edge, CancellationToken ct = default);

    /// <summary>Atomically add <paramref name="delta"/> to an edge's weight.
    /// Creates the edge at weight=<paramref name="delta"/> if it doesn't exist.</summary>
    Task IncrementEdgeWeightAsync(string fromId, string toId, string edgeType,
                                   double delta = 1.0, CancellationToken ct = default);

    // ── Traversal ─────────────────────────────────────────────
    /// <summary>Return all edges leaving <paramref name="nodeId"/>, optionally filtered by type.</summary>
    Task<IReadOnlyList<GraphEdge>> GetEdgesFromAsync(string nodeId, string? edgeType = null,
                                                      CancellationToken ct = default);

    /// <summary>Return all edges arriving at <paramref name="nodeId"/>.</summary>
    Task<IReadOnlyList<GraphEdge>> GetEdgesToAsync(string nodeId, string? edgeType = null,
                                                    CancellationToken ct = default);

    /// <summary>BFS expansion: start from <paramref name="seedIds"/>, follow edges of
    /// <paramref name="edgeType"/>, up to <paramref name="maxHops"/> hops.
    /// Returns the IDs of all reachable nodes (excluding seeds).</summary>
    Task<IReadOnlyList<string>> ExpandAsync(IEnumerable<string> seedIds,
                                             string edgeType,
                                             int maxHops = 2,
                                             CancellationToken ct = default);

    /// <summary>Return all Photo node IDs connected to a given Tag node.</summary>
    Task<IReadOnlyList<string>> GetPhotosByTagNodeAsync(string tagNodeId,
                                                         CancellationToken ct = default);

    // ── Stats ─────────────────────────────────────────────────
    Task<int> CountNodesAsync(string? nodeType = null, CancellationToken ct = default);
    Task<int> CountEdgesAsync(string? edgeType = null, CancellationToken ct = default);
}
