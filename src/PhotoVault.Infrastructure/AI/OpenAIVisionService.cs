using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoVault.Core.Domain;
using PhotoVault.Core.Interfaces;

namespace PhotoVault.Infrastructure.AI;

public class OpenAIVisionService : IAIService
{
    public string ModelName => "gpt-4o";

    private readonly HttpClient              _http;
    private readonly OpenAIOptions           _opts;
    private readonly IImageAnalysisService   _local;
    private readonly ILogger<OpenAIVisionService> _log;

    // Cost-control: max concurrent GPT-4o calls
    private static readonly SemaphoreSlim _throttle = new(4, 4);

    public OpenAIVisionService(HttpClient http, IOptions<OpenAIOptions> opts,
                               IImageAnalysisService local,
                               ILogger<OpenAIVisionService> log)
    {
        _http  = http;
        _opts  = opts.Value;
        _local = local;
        _log   = log;
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _opts.ApiKey);
        _http.BaseAddress = new Uri("https://api.openai.com/v1/");
        _http.Timeout     = TimeSpan.FromSeconds(60);
    }

    // ── Tag + Caption ─────────────────────────────────────────
    public async Task<AITagResult> TagAsync(string absoluteImagePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opts.ApiKey) || _opts.ApiKey.StartsWith("YOUR_"))
        {
            _log.LogWarning("OpenAI API key not configured — skipping tagging for {Path}", absoluteImagePath);
            return new AITagResult([], null);
        }

        var base64   = await ToBase64Async(absoluteImagePath, ct);
        var mimeType = GetMimeType(absoluteImagePath);

        var body = new
        {
            model      = ModelName,
            max_tokens = 600,
            messages   = new[]
            {
                new {
                    role    = "user",
                    content = new object[]
                    {
                        new { type = "text", text =
                            "Analyze this photo. Return ONLY valid JSON (no markdown, no explanation):\n" +
                            "{\"caption\":\"one sentence description\",\"scene\":\"one of: beach|mountains|city|food|nature|people|indoor|travel|sport|event|other\",\"tags\":[{\"name\":\"tag\",\"category\":\"Object|Person|Place|Event|Emotion|General\",\"confidence\":0.9}]}\n" +
                            "Rules: max 20 tags, lowercase tag names, confidence 0.0-1.0, be specific." },
                        new { type = "image_url", image_url = new { url = $"data:{mimeType};base64,{base64}", detail = "low" } }
                    }
                }
            }
        };

        await _throttle.WaitAsync(ct);
        try
        {
            var response = await PostAsync("chat/completions", body, ct);
            var raw      = response.RootElement
                .GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";

            // Strip markdown fences
            raw = raw.Trim();
            if (raw.StartsWith("```")) raw = raw.Split('\n', 2)[1];
            if (raw.StartsWith("json")) raw = raw[4..];
            raw = raw.TrimEnd('`').Trim();

            using var doc    = JsonDocument.Parse(raw);
            var caption      = doc.RootElement.TryGetProperty("caption", out var c) ? c.GetString() : null;
            var scene        = doc.RootElement.TryGetProperty("scene",   out var s) ? s.GetString() : null;
            var tags         = new List<TagPrediction>();

            if (doc.RootElement.TryGetProperty("tags", out var tagsEl))
            {
                foreach (var t in tagsEl.EnumerateArray())
                {
                    var name   = t.GetProperty("name").GetString() ?? "";
                    var catStr = t.TryGetProperty("category", out var cat) ? cat.GetString() : "General";
                    var conf   = t.TryGetProperty("confidence", out var cv) ? cv.GetDouble() : 0.8;
                    if (Enum.TryParse<TagCategory>(catStr, true, out var category))
                        tags.Add(new TagPrediction(name, category, conf));
                }
            }

            // Add scene as a Place tag
            if (!string.IsNullOrWhiteSpace(scene))
                tags.Add(new TagPrediction(scene, TagCategory.Place, 0.95));

            _log.LogDebug("Tagged {Path}: {Count} tags, scene={Scene}", absoluteImagePath, tags.Count, scene);
            return new AITagResult(tags, caption);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "GPT-4o tagging failed for {Path}", absoluteImagePath);
            return new AITagResult([], null);
        }
        finally
        {
            _throttle.Release();
        }
    }

    public async Task<string> CaptionAsync(string absoluteImagePath, CancellationToken ct = default)
        => (await TagAsync(absoluteImagePath, ct)).Caption ?? "No caption generated.";

    // ── Embedding ─────────────────────────────────────────────
    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opts.ApiKey) || _opts.ApiKey.StartsWith("YOUR_"))
            return [];

        var body     = new { model = "text-embedding-3-small", input = text };
        var response = await PostAsync("embeddings", body, ct);
        var data     = response.RootElement.GetProperty("data")[0].GetProperty("embedding");
        return data.EnumerateArray().Select(e => e.GetSingle()).ToArray();
    }

    // ── Blur detection (local — no API cost) ─────────────────
    public async Task<bool> IsBlurryAsync(string absoluteImagePath, CancellationToken ct = default)
    {
        var score = await _local.ComputeBlurScoreAsync(absoluteImagePath, ct);
        return score < 100.0; // threshold: < 100 = blurry
    }

    // ── Face detection (stub — Phase 5) ──────────────────────
    public Task<IReadOnlyList<FaceDetection>> DetectFacesAsync(string absoluteImagePath,
                                                                CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<FaceDetection>>([]);

    // ── Helpers ───────────────────────────────────────────────
    private async Task<JsonDocument> PostAsync(string endpoint, object body, CancellationToken ct)
    {
        var json    = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var resp    = await _http.PostAsync(endpoint, content, ct);
        resp.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
    }

    private static async Task<string> ToBase64Async(string path, CancellationToken ct)
        => Convert.ToBase64String(await File.ReadAllBytesAsync(path, ct));

    private static string GetMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png"  => "image/png",
        ".gif"  => "image/gif",
        ".webp" => "image/webp",
        ".heic" => "image/heic",
        _       => "image/jpeg"
    };
}

public class OpenAIOptions
{
    public const string Section = "OpenAI";
    public string ApiKey { get; set; } = default!;
}
