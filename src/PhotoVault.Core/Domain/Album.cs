namespace PhotoVault.Core.Domain;

public class Album
{
    public string  Id              { get; set; } = Guid.NewGuid().ToString();
    public string  Name            { get; set; } = default!;
    public string? Description     { get; set; }
    public string? CoverMediaId    { get; set; }
    public AlbumType AlbumType     { get; set; } = AlbumType.User;
    public string? CreatedByUserId { get; set; }
    public DateTime CreatedAt      { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt      { get; set; } = DateTime.UtcNow;
    public bool    IsShared        { get; set; }
    public string? ShareToken      { get; set; }
    public List<Media> Media       { get; set; } = [];
}

public enum AlbumType { User, AI, Smart, System }
