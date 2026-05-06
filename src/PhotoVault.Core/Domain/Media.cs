namespace PhotoVault.Core.Domain;

public class Media
{
    public string  Id              { get; set; } = Guid.NewGuid().ToString();
    public string  FileName        { get; set; } = default!;
    public string  OriginalPath    { get; set; } = default!;   // relative to MediaRoot
    public string  FileHash        { get; set; } = default!;
    public string? PerceptualHash  { get; set; }
    public MediaType MediaType     { get; set; } = MediaType.Unknown;
    public string? MimeType        { get; set; }
    public long    FileSizeBytes   { get; set; }
    public int?    Width           { get; set; }
    public int?    Height          { get; set; }
    public double? DurationSeconds { get; set; }
    public DateTime? CapturedAt   { get; set; }
    public double? Latitude        { get; set; }
    public double? Longitude       { get; set; }
    public string? CameraModel     { get; set; }
    public bool    IsBlurry        { get; set; }
    public double? BlurScore       { get; set; }
    public bool    IsDuplicate     { get; set; }
    public string? DuplicateOfId   { get; set; }
    public bool    InTrash         { get; set; }
    public DateTime? TrashedAt     { get; set; }
    public string?   TrashedByUserId { get; set; }
    public string?   TrashPath     { get; set; }
    public bool    AIProcessed     { get; set; }
    public DateTime? AIProcessedAt { get; set; }
    public string?   AIModelUsed   { get; set; }
    public DateTime CreatedAt      { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt      { get; set; } = DateTime.UtcNow;

    // Navigation
    public List<Tag> Tags          { get; set; } = [];
    public List<Thumbnail> Thumbnails { get; set; } = [];
}

public enum MediaType { Photo, Video, Unknown }
