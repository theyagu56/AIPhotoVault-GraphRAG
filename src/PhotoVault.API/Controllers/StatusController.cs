using Microsoft.AspNetCore.Mvc;

namespace PhotoVault.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StatusController : ControllerBase
{
    // GET /api/status
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        status    = "running",
        version   = "1.0.0",
        phase     = "Phase 1 — Core Infrastructure",
        timestamp = DateTime.UtcNow,
        features  = new[]
        {
            "Media library scanning",
            "File watching (LaCie MediaRoot)",
            "SQLite database",
            "Background processing queue",
            "Thumbnail service (stub — Phase 4)",
            "AI tagging (stub — Phase 5)"
        },
        endpoints = new[]
        {
            "GET  /api/status",
            "GET  /api/media",
            "GET  /api/media/{id}",
            "GET  /api/media/stats",
            "DELETE /api/media/{id}",
            "POST /api/media/{id}/restore",
            "GET  /api/album",
            "GET  /api/album/{id}",
            "POST /api/album",
            "DELETE /api/album/{id}",
            "POST /api/album/{id}/media/{mediaId}",
            "DELETE /api/album/{id}/media/{mediaId}"
        }
    });
}
