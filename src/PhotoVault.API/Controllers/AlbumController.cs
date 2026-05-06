using Microsoft.AspNetCore.Mvc;
using PhotoVault.Core.Domain;
using PhotoVault.Core.Interfaces;

namespace PhotoVault.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AlbumController : ControllerBase
{
    private readonly IAlbumRepository _albums;
    private readonly ILogger<AlbumController> _log;

    public AlbumController(IAlbumRepository albums, ILogger<AlbumController> log)
    {
        _albums = albums;
        _log    = log;
    }

    // GET /api/album?userId=xxx
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string userId = "system",
                                          CancellationToken ct = default)
    {
        var albums = await _albums.GetByUserAsync(userId, ct);
        return Ok(albums);
    }

    // GET /api/album/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct = default)
    {
        var album = await _albums.GetByIdAsync(id, ct);
        return album is null ? NotFound() : Ok(album);
    }

    // POST /api/album
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAlbumRequest req,
                                             CancellationToken ct = default)
    {
        var album = new Album
        {
            Id        = Guid.NewGuid().ToString(),
            Name      = req.Name,
            CreatedByUserId = req.UserId ?? "system",
            AlbumType = AlbumType.User,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var id = await _albums.CreateAsync(album, ct);
        return CreatedAtAction(nameof(GetById), new { id }, album);
    }

    // DELETE /api/album/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct = default)
    {
        var album = await _albums.GetByIdAsync(id, ct);
        if (album is null) return NotFound();
        await _albums.DeleteAsync(id, ct);
        return NoContent();
    }

    // POST /api/album/{id}/media/{mediaId}
    [HttpPost("{id}/media/{mediaId}")]
    public async Task<IActionResult> AddMedia(string id, string mediaId,
                                               CancellationToken ct = default)
    {
        await _albums.AddMediaAsync(id, mediaId, ct);
        return NoContent();
    }

    // DELETE /api/album/{id}/media/{mediaId}
    [HttpDelete("{id}/media/{mediaId}")]
    public async Task<IActionResult> RemoveMedia(string id, string mediaId,
                                                  CancellationToken ct = default)
    {
        await _albums.RemoveMediaAsync(id, mediaId, ct);
        return NoContent();
    }
}

public record CreateAlbumRequest(string Name, string? UserId);
