using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using PhotoVault.Core.Domain;
using PhotoVault.Core.Interfaces;

namespace PhotoVault.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IUserRepository  _users;
    private readonly IConfiguration   _config;
    private readonly ILogger<AuthController> _log;
    private readonly HttpClient       _http;

    public AuthController(IUserRepository users, IConfiguration config,
                          ILogger<AuthController> log, IHttpClientFactory httpFactory)
    {
        _users  = users;
        _config = config;
        _log    = log;
        _http   = httpFactory.CreateClient();
    }

    // ── GET /api/auth/me ─────────────────────────────────────────
    /// Returns the currently authenticated user (via JWT Bearer)
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();

        var user = await _users.GetByIdAsync(userId, ct);
        return user is null ? NotFound() : Ok(new
        {
            user.Id, user.Email, user.DisplayName, user.PhotoUrl,
            role   = user.Role.ToString(),
            user.ApprovedAt
        });
    }

    // ── POST /api/auth/google ─────────────────────────────────────
    /// Exchange Google ID token for a PhotoVault JWT
    [HttpPost("google")]
    public async Task<IActionResult> GoogleSignIn([FromBody] GoogleSignInRequest req,
                                                   CancellationToken ct)
    {
        // Verify token with Google
        GoogleUserInfo? googleUser;
        try
        {
            var resp = await _http.GetStringAsync(
                $"https://www.googleapis.com/oauth2/v3/tokeninfo?id_token={req.IdToken}", ct);
            googleUser = JsonSerializer.Deserialize<GoogleUserInfo>(resp,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Google token verification failed");
            return Unauthorized(new { error = "Invalid Google token" });
        }

        if (googleUser?.Sub is null)
            return Unauthorized(new { error = "Could not read Google user info" });

        // Upsert user in DB
        var existing = await _users.GetByIdAsync(googleUser.Sub, ct);
        User user;

        if (existing is null)
        {
            // First sign-in → create as Pending, wait for admin approval
            user = new User
            {
                Id             = googleUser.Sub,
                Email          = googleUser.Email ?? "",
                DisplayName    = googleUser.Name ?? googleUser.Email ?? "",
                PhotoUrl       = googleUser.Picture,
                Role           = UserRole.Pending,
                CreatedAt      = DateTime.UtcNow
            };
            await _users.UpsertAsync(user, ct);
            _log.LogInformation("New user registered (Pending): {Email}", user.Email);
            return Ok(new { status = "pending", message = "Your account is awaiting admin approval." });
        }

        user = existing;

        if (user.Role == UserRole.Rejected)
            return Forbid();

        if (user.Role == UserRole.Pending)
            return Ok(new { status = "pending", message = "Your account is awaiting admin approval." });

        // Approved — issue JWT
        var token = IssueJwt(user);
        return Ok(new
        {
            status = "ok",
            token,
            user   = new { user.Id, user.Email, user.DisplayName, user.PhotoUrl,
                           role = user.Role.ToString() }
        });
    }

    // ── POST /api/auth/dev-login  (dev only — remove in production) ──
    [HttpPost("dev-login")]
    public async Task<IActionResult> DevLogin([FromBody] DevLoginRequest req,
                                               CancellationToken ct)
    {
        if (!HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>()
                .IsDevelopment()
            && _config["AllowDevLogin"] != "true")
            return NotFound();

        var user = await _users.GetByEmailAsync(req.Email, ct)
                   ?? new User
                   {
                       Id          = Guid.NewGuid().ToString(),
                       Email       = req.Email,
                       DisplayName = req.Email.Split('@')[0],
                       Role        = UserRole.Admin,
                       CreatedAt   = DateTime.UtcNow
                   };

        if (user.Role == UserRole.Pending || user.Role == UserRole.Rejected)
            user.Role = UserRole.Admin; // auto-elevate in dev

        // Ensure user exists in DB
        var exists = await _users.GetByIdAsync(user.Id, ct);
        if (exists is null) await _users.UpsertAsync(user, ct);

        var token = IssueJwt(user);
        return Ok(new { status = "ok", token,
            user = new { user.Id, user.Email, user.DisplayName, role = user.Role.ToString() } });
    }

    // ─────────────────────────────────────────────────────────────
    private string IssueJwt(User user)
    {
        var secret  = _config["Jwt:Secret"] ?? "photovault-dev-secret-change-in-production-32chars";
        var key     = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds   = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddDays(30);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email,          user.Email),
            new Claim(ClaimTypes.Name,           user.DisplayName ?? ""),
            new Claim("photovault:role",         user.Role.ToString())
        };

        var jwt = new JwtSecurityToken(
            issuer:   "photovault",
            audience: "photovault",
            claims:   claims,
            expires:  expires,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}

public record GoogleSignInRequest(string IdToken);
public record DevLoginRequest(string Email);

// Google tokeninfo response shape
file sealed class GoogleUserInfo
{
    public string? Sub     { get; set; }
    public string? Email   { get; set; }
    public string? Name    { get; set; }
    public string? Picture { get; set; }
}
