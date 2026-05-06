using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using PhotoVault.BackgroundServices;
using PhotoVault.Core.Interfaces;
using PhotoVault.Core.Pipeline;
using PhotoVault.Infrastructure.AI;
using PhotoVault.Infrastructure.FileSystem;
using PhotoVault.Infrastructure.Repositories;
using PhotoVault.Infrastructure.Stubs;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

// ── Config ────────────────────────────────────────────────────
builder.Services.Configure<MediaRootOptions>(
    builder.Configuration.GetSection(MediaRootOptions.Section));
builder.Services.Configure<OpenAIOptions>(
    builder.Configuration.GetSection(OpenAIOptions.Section));

// ── Core services ─────────────────────────────────────────────
builder.Services.AddSingleton<IMediaProcessingQueue, InMemoryProcessingQueue>();
builder.Services.AddSingleton<IFileStorageService,   LocalFileStorageService>();
builder.Services.AddSingleton<IMediaRepository,      MediaRepository>();
builder.Services.AddSingleton<IUserRepository,       UserRepository>();
builder.Services.AddSingleton<IAlbumRepository,      AlbumRepository>();
builder.Services.AddSingleton<ITagRepository,        TagRepository>();
builder.Services.AddSingleton<IThumbnailService,     NullThumbnailService>(); // Phase 5: swap → ImageSharpThumbnailService

// ── Local image analysis (blur + pHash + EXIF) — no API cost ─
builder.Services.AddSingleton<IImageAnalysisService, ImageAnalysisService>();

// ── GraphRAG — 100% local knowledge graph ────────────────────
builder.Services.AddSingleton<IGraphRepository, GraphRepository>();
builder.Services.AddSingleton<GraphIndexService>();

// ── AI (GPT-4o Vision) ────────────────────────────────────────
builder.Services.AddHttpClient<IAIService, OpenAIVisionService>();

// ── Background services ───────────────────────────────────────
builder.Services.AddHostedService<FileWatcherService>();
builder.Services.AddHostedService<MediaProcessingWorker>();
builder.Services.AddHostedService<AutoAlbumService>();

// ── JWT Bearer ────────────────────────────────────────────────
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? "photovault-dev-secret-change-in-production-32chars!!";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = "photovault",
            ValidAudience            = "photovault",
            IssuerSigningKey         = new SymmetricSecurityKey(
                                           Encoding.UTF8.GetBytes(jwtSecret))
        };
    });

// ── Google OAuth only when real credentials are present ───────
var googleClientId     = builder.Configuration["Google:ClientId"] ?? "";
var googleClientSecret = builder.Configuration["Google:ClientSecret"] ?? "";
var hasGoogleAuth      = googleClientId.Length > 0 && !googleClientId.StartsWith("YOUR_");

if (hasGoogleAuth)
{
    builder.Services.AddAuthentication()
        .AddGoogle(g =>
        {
            g.ClientId     = googleClientId;
            g.ClientSecret = googleClientSecret;
        });
}

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ApprovedUser", p => p.RequireClaim("photovault:role", "Admin", "User"));
    options.AddPolicy("AdminOnly",    p => p.RequireClaim("photovault:role", "Admin"));
});

// ── API ───────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "PhotoVault API", Version = "v1",
        Description = "Local-first AI photo & video album" });
});
builder.Services.AddCors(c => c.AddDefaultPolicy(p =>
    p.WithOrigins(builder.Configuration["Frontend:Origin"] ?? "http://localhost:3000")
     .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

// ── Middleware ────────────────────────────────────────────────
// Swagger always enabled (Phase 1 dev — restrict in production later)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "PhotoVault API v1");
    c.RoutePrefix = "swagger";
});

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Health-check endpoint so we can verify the API is alive
app.MapGet("/health", () => new { status = "ok", version = "1.0", phase = "Phase 1" });

// ── DB init on first boot ─────────────────────────────────────
await EnsureDatabaseAsync(app);

app.Run();

static async Task EnsureDatabaseAsync(WebApplication app)
{
    var opts       = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<MediaRootOptions>>();
    var mediaRoot  = opts.Value.MediaRoot;

    // If configured MediaRoot is unavailable (e.g. external drive not mounted),
    // fall back to a local tmp path so the API still starts.
    if (!Directory.Exists(Path.GetPathRoot(mediaRoot)) &&
        !Directory.Exists(mediaRoot))
    {
        var fallback = Path.Combine(Path.GetTempPath(), "photovault-dev", "MediaRoot");
        app.Logger.LogWarning(
            "⚠️  MediaRoot {Root} is not accessible — falling back to {Fallback}",
            mediaRoot, fallback);
        mediaRoot = fallback;
    }

    var dbDir      = Path.Combine(mediaRoot, "Application", "Database");
    var dbPath     = Path.Combine(dbDir, "photovault.db");
    var schemaPath = Path.Combine(AppContext.BaseDirectory, "schema.sql");

    try
    {
        Directory.CreateDirectory(dbDir);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "❌ Could not create database directory {Dir}", dbDir);
        return;
    }

    if (!File.Exists(dbPath) && File.Exists(schemaPath))
    {
        var schema = await File.ReadAllTextAsync(schemaPath);
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = schema;
        await cmd.ExecuteNonQueryAsync();
        app.Logger.LogInformation("✅ Database initialised at {Path}", dbPath);
    }
    else
    {
        app.Logger.LogInformation("✅ Database ready at {Path}", dbPath);
    }
}
