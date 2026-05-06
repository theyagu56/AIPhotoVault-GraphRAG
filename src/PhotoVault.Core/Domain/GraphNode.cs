namespace PhotoVault.Core.Domain;

public class GraphNode
{
    public string  Id       { get; set; } = default!;
    public string  NodeType { get; set; } = default!;   // Photo | Tag | Location | Event | Album
    public string  Label    { get; set; } = default!;
    public string? Metadata { get; set; }               // JSON blob
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class GraphEdge
{
    public string FromId   { get; set; } = default!;
    public string ToId     { get; set; } = default!;
    public string EdgeType { get; set; } = default!;   // hasTag | takenAt | partOf | relatedTo | near
    public double Weight   { get; set; } = 1.0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Well-known node type constants.</summary>
public static class NodeType
{
    public const string Photo    = "Photo";
    public const string Tag      = "Tag";
    public const string Location = "Location";
    public const string Event    = "Event";
    public const string Album    = "Album";
}

/// <summary>Well-known edge type constants.</summary>
public static class EdgeType
{
    public const string HasTag    = "hasTag";      // Photo → Tag
    public const string TakenAt   = "takenAt";     // Photo → Location
    public const string PartOf    = "partOf";      // Photo → Event | Album
    public const string RelatedTo = "relatedTo";   // Tag → Tag (co-occurrence)
    public const string Near      = "near";        // Location → Location
}
