namespace PhotoVault.Core.Domain;

public class FaceCluster
{
    public string  Id             { get; set; } = Guid.NewGuid().ToString();
    public string? Label          { get; set; }
    public string? CoverMediaId   { get; set; }
    public int     MediaCount     { get; set; }
    public DateTime CreatedAt     { get; set; } = DateTime.UtcNow;
    public List<FaceClusterMember> Members { get; set; } = [];
}

public class FaceClusterMember
{
    public string  Id          { get; set; } = Guid.NewGuid().ToString();
    public string  ClusterId   { get; set; } = default!;
    public string  MediaId     { get; set; } = default!;
    public BoundingBox? BoundingBox { get; set; }
    public float[] Embedding   { get; set; } = [];
    public double  Confidence  { get; set; } = 1.0;
}

public record BoundingBox(double X, double Y, double W, double H);
