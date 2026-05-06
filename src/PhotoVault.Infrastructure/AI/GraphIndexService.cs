using System.Text.Json;
using Microsoft.Extensions.Logging;
using PhotoVault.Core.Domain;
using PhotoVault.Core.Interfaces;

namespace PhotoVault.Infrastructure.AI;

/// <summary>
/// Builds and maintains the knowledge graph for a photo after it has been
/// tagged by the AI pipeline.
///
/// Nodes created per photo:
///   • Photo    — one per media item
///   • Tag      — one per unique tag name (shared across photos)
///   • Location — GPS cluster rounded to 2 dp (~1 km cell)
///   • Event    — temporal+spatial cluster (day + location cell)
///
/// Edges created:
///   • Photo  →[hasTag]    → Tag      (weight = confidence)
///   • Photo  →[takenAt]   → Location (weight = 1.0)
///   • Photo  →[partOf]    → Event    (weight = 1.0)
///   • Tag    →[relatedTo] → Tag      (weight += 1 per co-occurrence)
///   • Loc    →[near]      → Loc      (weight = 1/(dist+1), if within ~5 km)
/// </summary>
public sealed class GraphIndexService
{
    private readonly IGraphRepository _graph;
    private readonly ITagRepository   _tags;
    private readonly ILogger<GraphIndexService> _log;

    // ~5 km in decimal degrees latitude (~0.045°)
    private const double NearThresholdDeg = 0.045;

    public GraphIndexService(IGraphRepository graph, ITagRepository tags,
                              ILogger<GraphIndexService> log)
    {
        _graph = graph;
        _tags  = tags;
        _log   = log;
    }

    /// <summary>
    /// Index a fully-processed photo into the graph.
    /// Safe to call multiple times — all operations are upserts.
    /// </summary>
    public async Task IndexPhotoAsync(Media media, CancellationToken ct = default)
    {
        try
        {
            var photoNodeId = PhotoNodeId(media.Id);

            // ── 1. Photo node ─────────────────────────────────
            await _graph.UpsertNodeAsync(new GraphNode
            {
                Id       = photoNodeId,
                NodeType = NodeType.Photo,
                Label    = media.FileName,
                Metadata = JsonSerializer.Serialize(new
                {
                    mediaId    = media.Id,
                    capturedAt = media.CapturedAt?.ToString("O"),
                    latitude   = media.Latitude,
                    longitude  = media.Longitude,
                    isBlurry   = media.IsBlurry,
                    isDuplicate= media.IsDuplicate,
                })
            }, ct);

            // ── 2. Tags + hasTag edges + co-occurrence ────────
            var tagList = await _tags.GetTagsForMediaAsync(media.Id, ct);
            var tagNodeIds = new List<string>(tagList.Count);

            foreach (var tag in tagList)
            {
                var tagNodeId = TagNodeId(tag.Name);
                tagNodeIds.Add(tagNodeId);

                // Tag node
                await _graph.UpsertNodeAsync(new GraphNode
                {
                    Id       = tagNodeId,
                    NodeType = NodeType.Tag,
                    Label    = tag.Name,
                    Metadata = JsonSerializer.Serialize(new { category = tag.Category, source = tag.Source })
                }, ct);

                // Photo → hasTag → Tag
                await _graph.UpsertEdgeAsync(new GraphEdge
                {
                    FromId   = photoNodeId,
                    ToId     = tagNodeId,
                    EdgeType = EdgeType.HasTag,
                    Weight   = tag.Confidence ?? 1.0
                }, ct);
            }

            // Tag co-occurrence: increment relatedTo for every pair
            for (int i = 0; i < tagNodeIds.Count; i++)
            for (int j = i + 1; j < tagNodeIds.Count; j++)
            {
                await _graph.IncrementEdgeWeightAsync(
                    tagNodeIds[i], tagNodeIds[j], EdgeType.RelatedTo, 1.0, ct);
                await _graph.IncrementEdgeWeightAsync(
                    tagNodeIds[j], tagNodeIds[i], EdgeType.RelatedTo, 1.0, ct);
            }

            // ── 3. Location node + takenAt edge ──────────────
            if (media.Latitude is not null && media.Longitude is not null)
            {
                var locNodeId = await UpsertLocationNodeAsync(
                    media.Latitude.Value, media.Longitude.Value, ct);

                // Photo → takenAt → Location
                await _graph.UpsertEdgeAsync(new GraphEdge
                {
                    FromId   = photoNodeId,
                    ToId     = locNodeId,
                    EdgeType = EdgeType.TakenAt,
                    Weight   = 1.0
                }, ct);

                // ── 4. Event node + partOf edge ───────────────
                if (media.CapturedAt is not null)
                {
                    var eventNodeId = await UpsertEventNodeAsync(
                        media.CapturedAt.Value, locNodeId, ct);

                    await _graph.UpsertEdgeAsync(new GraphEdge
                    {
                        FromId   = photoNodeId,
                        ToId     = eventNodeId,
                        EdgeType = EdgeType.PartOf,
                        Weight   = 1.0
                    }, ct);
                }
            }
            else if (media.CapturedAt is not null)
            {
                // Date-only event (no GPS)
                var eventNodeId = await UpsertEventNodeAsync(media.CapturedAt.Value, null, ct);
                await _graph.UpsertEdgeAsync(new GraphEdge
                {
                    FromId   = photoNodeId,
                    ToId     = eventNodeId,
                    EdgeType = EdgeType.PartOf,
                    Weight   = 1.0
                }, ct);
            }

            _log.LogDebug("📊 Graph indexed: {Id} → {Tags} tags, loc={HasLoc}, event={HasDate}",
                media.Id, tagList.Count,
                media.Latitude.HasValue, media.CapturedAt.HasValue);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Graph indexing failed for {Id}", media.Id);
        }
    }

    // ── Helpers ───────────────────────────────────────────────

    private async Task<string> UpsertLocationNodeAsync(double lat, double lon,
                                                        CancellationToken ct)
    {
        // Round to 2 decimal places ≈ 1.1 km cell
        var rLat = Math.Round(lat, 2);
        var rLon = Math.Round(lon, 2);
        var nodeId = LocationNodeId(rLat, rLon);

        await _graph.UpsertNodeAsync(new GraphNode
        {
            Id       = nodeId,
            NodeType = NodeType.Location,
            Label    = $"{rLat:F2}°, {rLon:F2}°",
            Metadata = JsonSerializer.Serialize(new { latitude = rLat, longitude = rLon })
        }, ct);

        // Ensure near edges to adjacent ~5 km cells
        await LinkNearLocationNodesAsync(nodeId, rLat, rLon, ct);

        return nodeId;
    }

    private async Task LinkNearLocationNodesAsync(string nodeId, double rLat, double rLon,
                                                   CancellationToken ct)
    {
        // Check the 8 surrounding cells and any existing location nodes within threshold
        var edges = await _graph.GetEdgesFromAsync(nodeId, null, ct);
        var existingNeighbours = edges.Where(e => e.EdgeType == EdgeType.Near)
                                      .Select(e => e.ToId).ToHashSet();

        // Check cardinal + diagonal neighbours (0.05° step ≈ 5 km)
        foreach (var (dLat, dLon) in NeighbourDeltas())
        {
            var nLat = Math.Round(rLat + dLat, 2);
            var nLon = Math.Round(rLon + dLon, 2);
            var neighbour = LocationNodeId(nLat, nLon);

            if (existingNeighbours.Contains(neighbour)) continue;

            var exists = await _graph.GetNodeAsync(neighbour, ct);
            if (exists is null) continue;   // don't create phantom nodes

            var dist = ApproxDistanceDeg(rLat, rLon, nLat, nLon);
            var weight = 1.0 / (dist + 1.0);

            await _graph.UpsertEdgeAsync(new GraphEdge
                { FromId = nodeId,    ToId = neighbour, EdgeType = EdgeType.Near, Weight = weight }, ct);
            await _graph.UpsertEdgeAsync(new GraphEdge
                { FromId = neighbour, ToId = nodeId,    EdgeType = EdgeType.Near, Weight = weight }, ct);
        }
    }

    private async Task<string> UpsertEventNodeAsync(DateTime capturedAt, string? locationNodeId,
                                                      CancellationToken ct)
    {
        var dateStr   = capturedAt.Date.ToString("yyyy-MM-dd");
        var nodeId    = locationNodeId is not null
                        ? $"event:{dateStr}:{locationNodeId.Replace("location:", "")}"
                        : $"event:{dateStr}";
        var label     = locationNodeId is not null
                        ? $"{capturedAt.Date:MMMM d, yyyy} · {locationNodeId.Replace("location:", "")}"
                        : capturedAt.Date.ToString("MMMM d, yyyy");

        await _graph.UpsertNodeAsync(new GraphNode
        {
            Id       = nodeId,
            NodeType = NodeType.Event,
            Label    = label,
            Metadata = JsonSerializer.Serialize(new { date = dateStr, locationNodeId })
        }, ct);

        return nodeId;
    }

    // ── Static ID helpers ─────────────────────────────────────
    public static string PhotoNodeId(string mediaId)          => $"photo:{mediaId}";
    public static string TagNodeId(string tagName)            => $"tag:{tagName.ToLowerInvariant().Trim()}";
    public static string LocationNodeId(double rLat, double rLon) => $"location:{rLat:F2}:{rLon:F2}";

    private static IEnumerable<(double dLat, double dLon)> NeighbourDeltas()
    {
        double step = 0.05;
        return new[]
        {
            (step, 0), (-step, 0), (0, step), (0, -step),
            (step, step), (step, -step), (-step, step), (-step, -step)
        };
    }

    private static double ApproxDistanceDeg(double lat1, double lon1, double lat2, double lon2)
        => Math.Sqrt((lat2 - lat1) * (lat2 - lat1) + (lon2 - lon1) * (lon2 - lon1));
}
