namespace PhotoVault.Core.Domain;

public class User
{
    public string Id          { get; set; } = default!;  // Google sub
    public string Email       { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string? PhotoUrl   { get; set; }
    public UserRole Role      { get; set; } = UserRole.Pending;
    public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy   { get; set; }
    public DateTime? LastLoginAt{ get; set; }
    public bool IsActive        { get; set; } = true;
}

public enum UserRole { Admin, User, Pending, Rejected }
