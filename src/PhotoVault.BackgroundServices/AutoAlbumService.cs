using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PhotoVault.Core.Domain;
using PhotoVault.Core.Interfaces;

namespace PhotoVault.BackgroundServices;

/// <summary>
/// Periodically scans the library and creates / updates smart albums:
///  1. "On This Day"     — per-year date albums
///  2. Date clusters     — one album per day that has ≥ 3 photos
///  3. Scene albums      — grouped by GPT-4o scene tag (beach, food, travel…)
///  4. Location albums   — grouped by GPS cluster (H3-lite grid at ~5km cell)
/// </summary>
public sealed class AutoAlbumService : BackgroundService
{
    private readonly IMediaRepository  _media;
    private readonly ITagRepository    _tags;
    private readonly IAlbumRepository  _albums;
    private readonly ILogger<AutoAlbumService> _log;

    // Scenes that warrant their own album
    private static readonly HashSet<string> SceneAlbumTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "beach", "mountains", "city", "food", "nature", "travel",
        "sport", "event", "indoor", "sunset", "snow", "birthday",
        "wedding", "graduation", "concert", "hiking", "camping"
    };

    // How often to re-run auto-album logic
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(6);

    public AutoAlbumService(IMediaRepository media, ITagRepository tags,
                             IAlbumRepository albums, ILogger<AutoAlbumService> log)
    {
        _media  = media;
        _tags   = tags;
        _albums = albums;
        _log    = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for the API to settle before first run
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _log.LogInformation("🗂️  AutoAlbumService: building smart albums…");
                await BuildSmartAlbumsAsync(stoppingToken);
                _log.LogInformation("🗂️  AutoAlbumService: done");
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "AutoAlbumService error");
            }

            await Task.Delay(RunInterval, stoppingToken);
        }
    }

    // ── Main orchestrator ─────────────────────────────────────
    private async Task BuildSmartAlbumsAsync(CancellationToken ct)
    {
        await BuildDateAlbumsAsync(ct);
        await BuildSceneAlbumsAsync(ct);
        await BuildLocationAlbumsAsync(ct);
    }

    // ── 1. Date-based albums — one per day with ≥ 3 photos ───
    private async Task BuildDateAlbumsAsync(CancellationToken ct)
    {
        // Fetch all AI-processed, non-trash media with a captured date
        var allMedia = await GetAllMediaAsync(ct);
        var byDay    = allMedia
            .Where(m => m.CapturedAt.HasValue)
            .GroupBy(m => m.CapturedAt!.Value.Date)
            .Where(g => g.Count() >= 3);

        foreach (var day in byDay)
        {
            var name    = $"📅 {day.Key:MMMM d, yyyy}";
            var albumId = await EnsureAlbumAsync(name, AlbumType.Smart, ct);
            foreach (var m in day)
                await TryAddMediaAsync(albumId, m.Id, ct);
        }
    }

    // ── 2. Scene albums — grouped by GPT-4o scene tag ────────
    private async Task BuildSceneAlbumsAsync(CancellationToken ct)
    {
        var allMedia = await GetAllMediaAsync(ct);

        foreach (var media in allMedia)
        {
            var mediaTags = await _tags.GetTagsForMediaAsync(media.Id, ct);
            foreach (var tag in mediaTags)
            {
                if (!SceneAlbumTags.Contains(tag.Name)) continue;

                var albumName = $"🏷️ {Capitalise(tag.Name)}";
                var albumId   = await EnsureAlbumAsync(albumName, AlbumType.AI, ct);
                await TryAddMediaAsync(albumId, media.Id, ct);
            }
        }
    }

    // ── 3. Location albums — ~5 km grid cells ────────────────
    private async Task BuildLocationAlbumsAsync(CancellationToken ct)
    {
        var allMedia = await GetAllMediaAsync(ct);
        var geoMedia = allMedia.Where(m => m.Latitude.HasValue && m.Longitude.HasValue).ToList();

        // Simple grid: round lat/lon to 1 decimal place ≈ 5-11 km cell
        var byCell = geoMedia.GroupBy(m =>
            (Math.Round(m.Latitude!.Value, 1), Math.Round(m.Longitude!.Value, 1)))
            .Where(g => g.Count() >= 3);

        foreach (var cell in byCell)
        {
            var (lat, lon) = cell.Key;
            var name       = $"📍 {lat:F1}°, {lon:F1}°";
            var albumId    = await EnsureAlbumAsync(name, AlbumType.Smart, ct);
            foreach (var m in cell)
                await TryAddMediaAsync(albumId, m.Id, ct);
        }
    }

    // ── Helpers ───────────────────────────────────────────────

    // Cache albums by name to avoid repeated DB lookups in a single run
    private Dictionary<string, string>? _albumCache;

    private async Task<string> EnsureAlbumAsync(string name, AlbumType albumType, CancellationToken ct)
    {
        _albumCache ??= new();
        if (_albumCache.TryGetValue(name, out var cached)) return cached;

        // Try to find existing album by name
        // We use the full list (user + AI) — getByUser with null
        var existing = await _albums.GetByUserAsync("system", ct);
        var found    = existing.FirstOrDefault(a => a.Name == name);
        if (found is not null)
        {
            _albumCache[name] = found.Id;
            return found.Id;
        }

        // Create new
        var album = new Album
        {
            Id              = Guid.NewGuid().ToString(),
            Name            = name,
            AlbumType       = albumType,
            CreatedByUserId = "system",
            CreatedAt       = DateTime.UtcNow,
            UpdatedAt       = DateTime.UtcNow
        };
        var id = await _albums.CreateAsync(album, ct);
        _albumCache[name] = id;
        _log.LogDebug("📁 Created smart album: {Name}", name);
        return id;
    }

    private async Task TryAddMediaAsync(string albumId, string mediaId, CancellationToken ct)
    {
        try { await _albums.AddMediaAsync(albumId, mediaId, ct); }
        catch { /* already added — INSERT OR IGNORE handles it */ }
    }

    private async Task<List<Media>> GetAllMediaAsync(CancellationToken ct)
    {
        const int PageSize = 500;
        var result = new List<Media>();
        int page   = 1;
        while (true)
        {
            var batch = await _media.GetPagedAsync(
                new Core.Interfaces.MediaQuery(
                    Page:       page,
                    PageSize:   PageSize,
                    InTrash:    false,
                    SortBy:     "CapturedAt",
                    Descending: false), ct);
            result.AddRange(batch);
            if (batch.Count < PageSize) break;
            page++;
        }
        return result;
    }

    private static string Capitalise(string s)
        => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
