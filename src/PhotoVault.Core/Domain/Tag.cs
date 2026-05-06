namespace PhotoVault.Core.Domain;

public class Tag
{
    public string  Id            { get; set; } = Guid.NewGuid().ToString();
    public string  Name          { get; set; } = default!;
    public TagCategory Category  { get; set; } = TagCategory.General;
    public bool    IsAIGenerated { get; set; }
    public DateTime CreatedAt    { get; set; } = DateTime.UtcNow;
    // join data
    public double? Confidence    { get; set; }
    public TagSource Source      { get; set; } = TagSource.User;
}

public enum TagCategory { Object, Person, Place, Event, Emotion, General, AI, User }
public enum TagSource   { User, AI, System }
