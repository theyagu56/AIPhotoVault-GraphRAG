using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using PhotoVault.Core.Domain;
using PhotoVault.Core.Interfaces;
using PhotoVault.Core.Pipeline;
using PhotoVault.Infrastructure.AI;
using PhotoVault.Infrastructure.FileSystem;

namespace PhotoVault.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MediaController : ControllerBase
{
    private static readonly HashSet<string> PhotoExts = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".heic", ".gif", ".webp", ".bmp", ".tiff" };
    private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mov", ".avi", ".mkv", ".m4v", ".wmv" };

    private readonly IMediaRepository        _media;
    private readonly ITagRepository          _tags;
    private readonly IAlbumRepository        _albums;
    private readonly IFileStorageService     _storage;
    private readonly IMediaProcessingQueue   _queue;
    private readonly MediaRootOptions        _opts;
    private readonly ILogger<MediaController> _log;

    public MediaController(IMediaRepository media, ITagRepository tags,
                           IAlbumRepository albums,
                           IFileStorageService storage,
                           IMediaProcessingQueue queue,
                           IOptions<MediaRootOptions> opts,
                           ILogger<MediaController> log)
    {
        _media   = media;
        _tags    = tags;
        _albums  = albums;
        _storage = storage;
        _queue   = queue;
        _opts    = opts.Value;
        _log     = log;
    }

    // GET /api/media?page=1&pageSize=50&mediaType=Photo
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int page       = 1,
        [FromQuery] int pageSize   = 50,
        [FromQuery] string? search = null,
        [FromQuery] string? tag    = null,
        [FromQuery] string? albumId = null,
        [FromQuery] string? mediaType = null,
        [FromQuery] bool inTrash   = false,
        [FromQuery] string sortBy  = "CapturedAt",
        [FromQuery] bool desc      = true,
        CancellationToken ct = default)
    {
        var type = mediaType switch
        {
            "Photo" => (MediaType?)MediaType.Photo,
            "Video" => (MediaType?)MediaType.Video,
            _       => null
        };

        var query = new MediaQuery(page, pageSize, search, tag, albumId,
                                   type, inTrash, false, null, null, sortBy, desc);

        var items = await _media.GetPagedAsync(query, ct);
        var total = await _media.CountAsync(query, ct);

        return Ok(new
        {
            page,
            pageSize,
            total,
            totalPages = (int)Math.Ceiling(total / (double)pageSize),
            items
        });
    }

    // GET /api/media/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct = default)
    {
        var item = await _media.GetByIdAsync(id, ct);
        return item is null ? NotFound() : Ok(item);
    }

    // DELETE /api/media/{id}  (soft-delete → Trash)
    [HttpDelete("{id}")]
    public async Task<IActionResult> Trash(string id, CancellationToken ct = default)
    {
        var item = await _media.GetByIdAsync(id, ct);
        if (item is null) return NotFound();

        var trashPath = await _storage.MoveToTrashAsync(item.OriginalPath, ct);
        await _media.MoveToTrashAsync(id, "system", trashPath, ct);
        return NoContent();
    }

    // POST /api/media/{id}/restore
    [HttpPost("{id}/restore")]
    public async Task<IActionResult> Restore(string id, CancellationToken ct = default)
    {
        var item = await _media.GetByIdAsync(id, ct);
        if (item is null) return NotFound();
        if (!item.InTrash) return BadRequest(new { error = "Item is not in trash" });

        await _storage.RestoreFromTrashAsync(item.TrashPath!, item.OriginalPath, ct);
        await _media.RestoreFromTrashAsync(id, ct);
        return NoContent();
    }

    // GET /api/media/stats  — library summary
    [HttpGet("stats")]
    public async Task<IActionResult> Stats(CancellationToken ct = default)
    {
        var totalPhotos = await _media.CountAsync(new MediaQuery(MediaType: MediaType.Photo), ct);
        var totalVideos = await _media.CountAsync(new MediaQuery(MediaType: MediaType.Video), ct);
        var inTrash     = await _media.CountAsync(new MediaQuery(InTrash: true), ct);
        var unprocessed = await _media.CountAsync(new MediaQuery(), ct);

        return Ok(new
        {
            totalPhotos,
            totalVideos,
            totalMedia = totalPhotos + totalVideos,
            inTrash,
            mediaRoot  = "/Volumes/LaCie/ONE PHOTOS/AI Photo & Video Album/MediaRoot"
        });
    }

    // POST /api/media/scan  { "path": "/Volumes/LaCie/ONE PHOTOS" }
    [HttpPost("scan")]
    public async Task<IActionResult> Scan([FromBody] ScanMediaRequest req,
                                          CancellationToken ct = default)
    {
        var root = (req.Path ?? "").Trim();
        if (string.IsNullOrWhiteSpace(root))
            return BadRequest(new { error = "Path is required." });

        root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(root));
        if (!Directory.Exists(root))
            return BadRequest(new { error = "Folder does not exist.", path = root });

        int discovered = 0, imported = 0, skipped = 0, failed = 0;
        var newlyImported = new List<(string Id, string AbsPath)>();

        var dbPath = Path.Combine(_opts.MediaRoot, "Application", "Database", "photovault.db");
        await using var db = new SqliteConnection($"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared;");
        await db.OpenAsync(ct);

        var knownPaths = (await db.QueryAsync<string>("SELECT OriginalPath FROM Media")).ToHashSet(StringComparer.OrdinalIgnoreCase);

        await using var tx = await db.BeginTransactionAsync(ct);
        var insert = db.CreateCommand();
        insert.Transaction = (SqliteTransaction)tx;
        insert.CommandText = @"
            INSERT INTO Media (Id,FileName,OriginalPath,FileHash,MediaType,MimeType,FileSizeBytes,CapturedAt,CreatedAt,UpdatedAt)
            VALUES ($id,$fileName,$originalPath,$fileHash,$mediaType,$mimeType,$fileSizeBytes,$capturedAt,$createdAt,$updatedAt)";

        var idParam = insert.Parameters.Add("$id", SqliteType.Text);
        var fileNameParam = insert.Parameters.Add("$fileName", SqliteType.Text);
        var originalPathParam = insert.Parameters.Add("$originalPath", SqliteType.Text);
        var fileHashParam = insert.Parameters.Add("$fileHash", SqliteType.Text);
        var mediaTypeParam = insert.Parameters.Add("$mediaType", SqliteType.Text);
        var mimeTypeParam = insert.Parameters.Add("$mimeType", SqliteType.Text);
        var fileSizeBytesParam = insert.Parameters.Add("$fileSizeBytes", SqliteType.Integer);
        var capturedAtParam = insert.Parameters.Add("$capturedAt", SqliteType.Text);
        var createdAtParam = insert.Parameters.Add("$createdAt", SqliteType.Text);
        var updatedAtParam = insert.Parameters.Add("$updatedAt", SqliteType.Text);

        foreach (var path in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            if (ct.IsCancellationRequested) break;
            if (!IsSupportedMedia(path)) continue;
            if (Path.GetFileName(path).StartsWith("._", StringComparison.Ordinal)) continue;
            if (path.Contains($"{Path.DirectorySeparatorChar}Application{Path.DirectorySeparatorChar}Database{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase)) continue;

            discovered++;
            try
            {
                var originalPath = Path.GetFullPath(path);
                if (knownPaths.Contains(originalPath))
                {
                    skipped++;
                    continue;
                }

                var file = new FileInfo(originalPath);
                var now = DateTime.UtcNow.ToString("O");
                idParam.Value = Guid.NewGuid().ToString();
                fileNameParam.Value = file.Name;
                originalPathParam.Value = originalPath;
                fileHashParam.Value = FastFingerprint(originalPath, file);
                mediaTypeParam.Value = DetectMediaType(originalPath).ToString();
                mimeTypeParam.Value = GetMimeType(originalPath);
                fileSizeBytesParam.Value = file.Length;
                capturedAtParam.Value = file.CreationTimeUtc.ToString("O");
                createdAtParam.Value = now;
                updatedAtParam.Value = now;

                var newId = (string)idParam.Value!;
                await insert.ExecuteNonQueryAsync(ct);
                knownPaths.Add(originalPath);
                newlyImported.Add((newId, originalPath));
                imported++;
            }
            catch (Exception ex)
            {
                failed++;
                _log.LogWarning(ex, "Scan failed for {Path}", path);
            }
        }

        await tx.CommitAsync(ct);

        // Enqueue every newly imported file for AI processing (EXIF, blur, pHash, tagging)
        foreach (var (id, absPath) in newlyImported)
            await _queue.EnqueueAsync(MediaProcessingJob.Create(id, absPath), ct);

        return Ok(new { path = root, discovered, imported, skipped, failed, queued = newlyImported.Count });
    }

    // GET /api/media/{id}/file
    [HttpGet("{id}/file")]
    public async Task<IActionResult> FileById(string id, CancellationToken ct = default)
    {
        var item = await _media.GetByIdAsync(id, ct);
        if (item is null) return NotFound();

        var absolutePath = _storage.Resolve(item.OriginalPath);
        if (!System.IO.File.Exists(absolutePath)) return NotFound();

        var stream = System.IO.File.OpenRead(absolutePath);
        return File(stream, item.MimeType ?? GetMimeType(absolutePath), enableRangeProcessing: true);
    }

    // GET /api/media/{id}/thumbnail/{size}
    [HttpGet("{id}/thumbnail/{size}")]
    public Task<IActionResult> Thumbnail(string id, string size, CancellationToken ct = default)
        => FileById(id, ct);

    // GET /api/media/{id}/tags
    [HttpGet("{id}/tags")]
    public async Task<IActionResult> GetTags(string id, CancellationToken ct = default)
    {
        var tags    = await _tags.GetTagsForMediaAsync(id, ct);
        var caption = await _tags.GetCaptionAsync(id, ct);
        return Ok(new { tags, caption });
    }

    // GET /api/media/{id}/albums
    [HttpGet("{id}/albums")]
    public async Task<IActionResult> GetAlbums(string id, CancellationToken ct = default)
    {
        var albums = await _albums.GetByMediaIdAsync(id, ct);
        return Ok(albums);
    }

    // POST /api/media/{id}/refresh-exif  — immediately re-extract EXIF (no queue)
    [HttpPost("{id}/refresh-exif")]
    public async Task<IActionResult> RefreshExif(string id,
        [FromServices] IImageAnalysisService analysis,
        CancellationToken ct = default)
    {
        var item = await _media.GetByIdAsync(id, ct);
        if (item is null) return NotFound();

        var absolutePath = _storage.Resolve(item.OriginalPath);
        if (!System.IO.File.Exists(absolutePath))
            return NotFound(new { error = "File not found on disk", path = absolutePath });

        var exif = await analysis.ExtractExifAsync(absolutePath, ct);

        if (exif.DateTaken.HasValue)  item.CapturedAt  = exif.DateTaken;
        if (exif.Latitude.HasValue)   item.Latitude    = exif.Latitude;
        if (exif.Longitude.HasValue)  item.Longitude   = exif.Longitude;
        if (exif.CameraModel != null) item.CameraModel = exif.CameraModel;
        if (exif.Width.HasValue)      item.Width       = exif.Width;
        if (exif.Height.HasValue)     item.Height      = exif.Height;

        await _media.UpdateAsync(item, ct);

        return Ok(new {
            latitude    = item.Latitude,
            longitude   = item.Longitude,
            capturedAt  = item.CapturedAt,
            cameraModel = item.CameraModel,
            width       = item.Width,
            height      = item.Height
        });
    }

    // GET /api/media/graph/stats  — graph node + edge counts
    [HttpGet("graph/stats")]
    public async Task<IActionResult> GraphStats(
        [FromServices] IGraphRepository graph,
        CancellationToken ct = default)
    {
        var totalNodes    = await graph.CountNodesAsync(null, ct);
        var photoNodes    = await graph.CountNodesAsync(NodeType.Photo,    ct);
        var tagNodes      = await graph.CountNodesAsync(NodeType.Tag,      ct);
        var locationNodes = await graph.CountNodesAsync(NodeType.Location, ct);
        var eventNodes    = await graph.CountNodesAsync(NodeType.Event,    ct);
        var totalEdges    = await graph.CountEdgesAsync(null, ct);
        var hasTagEdges   = await graph.CountEdgesAsync(EdgeType.HasTag,    ct);
        var relatedEdges  = await graph.CountEdgesAsync(EdgeType.RelatedTo, ct);
        var takenAtEdges  = await graph.CountEdgesAsync(EdgeType.TakenAt,   ct);

        return Ok(new {
            nodes = new { total = totalNodes, photos = photoNodes, tags = tagNodes,
                          locations = locationNodes, events = eventNodes },
            edges = new { total = totalEdges, hasTag = hasTagEdges,
                          relatedTo = relatedEdges, takenAt = takenAtEdges }
        });
    }

    // POST /api/media/graph/reindex  — (re)index all AI-processed photos into the graph
    [HttpPost("graph/reindex")]
    public async Task<IActionResult> GraphReindex(
        [FromServices] IGraphRepository    graph,
        [FromServices] GraphIndexService   indexer,
        [FromQuery]    int                 batchSize = 500,
        CancellationToken ct = default)
    {
        var dbPath = Path.Combine(_opts.MediaRoot, "Application", "Database", "photovault.db");
        await using var db = new SqliteConnection($"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared;");
        await db.OpenAsync(ct);

        var rows = await db.QueryAsync<(string Id, string OriginalPath)>(
            "SELECT Id, OriginalPath FROM Media WHERE AIProcessed=1 AND InTrash=0 ORDER BY CapturedAt DESC LIMIT @batchSize",
            new { batchSize });

        int indexed = 0;
        foreach (var (id, _) in rows)
        {
            var media = await _media.GetByIdAsync(id, ct);
            if (media is null) continue;
            await indexer.IndexPhotoAsync(media, ct);
            indexed++;
        }

        _log.LogInformation("📊 Graph reindex complete: {Count} photos indexed", indexed);
        return Ok(new { indexed, message = $"Graph reindex complete — {indexed} photos indexed." });
    }

    // POST /api/media/reprocess  — queue ALL unprocessed media for AI pipeline
    // Use this once after upgrading to trigger blur/pHash/tagging on existing photos.
    [HttpPost("reprocess")]
    public async Task<IActionResult> Reprocess(
        [FromQuery] bool all = false,          // true = re-queue even already-processed
        [FromQuery] int batchSize = 500,
        CancellationToken ct = default)
    {
        var dbPath = Path.Combine(_opts.MediaRoot, "Application", "Database", "photovault.db");
        await using var db = new SqliteConnection($"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared;");
        await db.OpenAsync(ct);

        // Fetch media that needs processing: unprocessed, or ALL if forced
        var sql = all
            ? "SELECT Id, OriginalPath FROM Media WHERE InTrash=0 ORDER BY CreatedAt LIMIT @batchSize"
            : "SELECT Id, OriginalPath FROM Media WHERE AIProcessed=0 AND InTrash=0 ORDER BY CreatedAt LIMIT @batchSize";

        var rows = await db.QueryAsync<(string Id, string OriginalPath)>(sql, new { batchSize });
        int queued = 0;

        foreach (var (id, path) in rows)
        {
            // path is the absolute path stored at scan time
            if (!System.IO.File.Exists(path)) continue;
            await _queue.EnqueueAsync(MediaProcessingJob.Create(id, path), ct);
            queued++;
        }

        _log.LogInformation("🔄 Reprocess: queued {Count} media items for AI pipeline", queued);
        return Ok(new { queued, message = $"Queued {queued} items for processing. Check back in a few minutes." });
    }

    // GET /api/media/graph/search?q=beach&maxHops=2&page=1&pageSize=50
    // Semantic search: expands query via relatedTo edges and returns ranked photos.
    [HttpGet("graph/search")]
    public async Task<IActionResult> GraphSearch(
        [FromServices] IGraphRepository graph,
        [FromQuery] string q = "",
        [FromQuery] int maxHops = 2,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(new { error = "Query 'q' is required" });

        var qNorm  = q.Trim().ToLowerInvariant();
        var dbPath = Path.Combine(_opts.MediaRoot, "Application", "Database", "photovault.db");
        await using var db = new SqliteConnection($"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared;");

        // 1. Find seed tag nodes that exactly or partially match the query
        var seedTagIds = (await db.QueryAsync<string>(
            @"SELECT Id FROM GraphNodes
              WHERE NodeType = 'Tag' AND (Id = @exact OR Label LIKE @contains)
              ORDER BY CASE WHEN Id = @exact THEN 0 ELSE 1 END
              LIMIT 15",
            new { exact = $"tag:{qNorm}", contains = $"%{qNorm}%" }
        )).ToList();

        if (!seedTagIds.Any())
            return Ok(new {
                query = q, expandedTags = Array.Empty<string>(),
                total = 0, page, pageSize, totalPages = 0, items = Array.Empty<object>()
            });

        // 2. BFS expand via relatedTo edges
        var expandedNodeIds = await graph.ExpandAsync(seedTagIds, EdgeType.RelatedTo, maxHops, ct);

        // Resolve labels for the "also matched" chips shown in the UI
        var expandedLabels = new List<string>();
        foreach (var eid in expandedNodeIds.Take(12))
        {
            var node = await graph.GetNodeAsync(eid, ct);
            if (node is not null) expandedLabels.Add(node.Label);
        }

        var allTagIds = seedTagIds.Concat(expandedNodeIds).ToList();

        // 3. Score photos: seed-tag matches 2×, expanded/related matches 1×
        var photoScores = new Dictionary<string, double>();
        foreach (var tagId in allTagIds)
        {
            double boost = seedTagIds.Contains(tagId) ? 2.0 : 1.0;
            var edges = await graph.GetEdgesToAsync(tagId, EdgeType.HasTag, ct);
            foreach (var e in edges)
            {
                var photoId = e.FromId.Replace("photo:", "");
                photoScores[photoId] = photoScores.GetValueOrDefault(photoId) + (e.Weight * boost);
            }
        }

        var rankedIds = photoScores
            .OrderByDescending(kv => kv.Value)
            .Select(kv => kv.Key)
            .ToList();

        int total   = rankedIds.Count;
        var pageIds = rankedIds.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var items = new List<Media>();
        foreach (var pid in pageIds)
        {
            var m = await _media.GetByIdAsync(pid, ct);
            if (m is not null && !m.InTrash) items.Add(m);
        }

        return Ok(new {
            query        = q,
            expandedTags = expandedLabels,
            total,
            page,
            pageSize,
            totalPages   = (int)Math.Ceiling(total / (double)pageSize),
            items
        });
    }

    // GET /api/media/{id}/similar?limit=12
    // Returns photos that share similar tags, ranked by weighted tag overlap + 1-hop graph expansion.
    [HttpGet("{id}/similar")]
    public async Task<IActionResult> SimilarPhotos(
        string id,
        [FromServices] IGraphRepository graph,
        [FromQuery] int limit = 12,
        CancellationToken ct = default)
    {
        var photoNodeId = GraphIndexService.PhotoNodeId(id);

        // 1. Get this photo's direct tag edges
        var tagEdges   = await graph.GetEdgesFromAsync(photoNodeId, EdgeType.HasTag, ct);
        var tagNodeIds = tagEdges.Select(e => e.ToId).ToList();

        if (!tagNodeIds.Any())
            return Ok(new { items = Array.Empty<object>() });

        // 2. One-hop expand via relatedTo for richer semantic coverage
        var relatedTagIds = await graph.ExpandAsync(tagNodeIds, EdgeType.RelatedTo, 1, ct);

        // Direct tags 2× boost (weighted by AI confidence); related tags 1×
        var scoredTags = tagEdges.Select(e => (tagId: e.ToId, boost: e.Weight * 2.0))
                          .Concat(relatedTagIds.Select(t => (tagId: t, boost: 1.0)))
                          .ToList();

        // 3. Score all other photos by weighted tag overlap
        var photoScores = new Dictionary<string, double>();
        foreach (var (tagId, boost) in scoredTags)
        {
            var edges = await graph.GetEdgesToAsync(tagId, EdgeType.HasTag, ct);
            foreach (var e in edges)
            {
                var otherId = e.FromId.Replace("photo:", "");
                if (otherId == id) continue;
                photoScores[otherId] = photoScores.GetValueOrDefault(otherId) + (e.Weight * boost);
            }
        }

        var topIds = photoScores
            .OrderByDescending(kv => kv.Value)
            .Take(limit)
            .Select(kv => kv.Key)
            .ToList();

        var items = new List<Media>();
        foreach (var pid in topIds)
        {
            var m = await _media.GetByIdAsync(pid, ct);
            if (m is not null && !m.InTrash) items.Add(m);
        }

        return Ok(new { items });
    }

    // GET /api/media/blurry?page=1&pageSize=50
    [HttpGet("blurry")]
    public async Task<IActionResult> Blurry(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 48,
        CancellationToken ct = default)
    {
        var items = await _media.GetBlurryAsync(page, pageSize, ct);
        return Ok(new { page, pageSize, total = items.Count, items });
    }

    // GET /api/media/duplicates?page=1&pageSize=50
    [HttpGet("duplicates")]
    public async Task<IActionResult> Duplicates(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 48,
        CancellationToken ct = default)
    {
        var items = await _media.GetDuplicatesAsync(page, pageSize, ct);
        return Ok(new { page, pageSize, total = items.Count, items });
    }

    private static bool IsSupportedMedia(string path)
        => PhotoExts.Contains(Path.GetExtension(path)) || VideoExts.Contains(Path.GetExtension(path));

    private static MediaType DetectMediaType(string path)
    {
        var ext = Path.GetExtension(path);
        if (PhotoExts.Contains(ext)) return MediaType.Photo;
        if (VideoExts.Contains(ext)) return MediaType.Video;
        return MediaType.Unknown;
    }

    private static string GetMimeType(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".tiff" => "image/tiff",
            ".heic" => "image/heic",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".avi" => "video/x-msvideo",
            ".mkv" => "video/x-matroska",
            ".m4v" => "video/x-m4v",
            ".wmv" => "video/x-ms-wmv",
            _ => "application/octet-stream"
        };

    private static string FastFingerprint(string path, FileInfo file)
    {
        var source = $"{path}|{file.Length}|{file.LastWriteTimeUtc:O}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
    }
}

public record ScanMediaRequest(string Path);
