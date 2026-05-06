using Microsoft.AspNetCore.Mvc;
using PhotoVault.Core.Domain;
using PhotoVault.Core.Interfaces;

namespace PhotoVault.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UserController : ControllerBase
{
    private readonly IUserRepository _users;
    private readonly ILogger<UserController> _log;

    public UserController(IUserRepository users, ILogger<UserController> log)
    {
        _users = users;
        _log   = log;
    }

    // GET /api/user/pending  — list users awaiting approval
    [HttpGet("pending")]
    public async Task<IActionResult> GetPending(CancellationToken ct)
    {
        var users = await _users.GetPendingAsync(ct);
        return Ok(users.Select(u => new
        {
            u.Id, u.Email, u.DisplayName, u.PhotoUrl, u.CreatedAt
        }));
    }

    // GET /api/user  — list all users (admin)
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var users = await _users.GetAllAsync(ct);
        return Ok(users.Select(u => new
        {
            u.Id, u.Email, u.DisplayName, u.PhotoUrl,
            role = u.Role.ToString(), u.CreatedAt, u.ApprovedAt
        }));
    }

    // POST /api/user/{id}/approve
    [HttpPost("{id}/approve")]
    public async Task<IActionResult> Approve(string id, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(id, ct);
        if (user is null) return NotFound();

        var approverId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                        ?? "system";

        await _users.ApproveAsync(id, approverId, ct);
        _log.LogInformation("User {Email} approved by {Approver}", user.Email, approverId);
        return NoContent();
    }

    // POST /api/user/{id}/reject
    [HttpPost("{id}/reject")]
    public async Task<IActionResult> Reject(string id, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(id, ct);
        if (user is null) return NotFound();

        var approverId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                        ?? "system";

        await _users.RejectAsync(id, approverId, ct);
        _log.LogInformation("User {Email} rejected", user.Email);
        return NoContent();
    }
}
