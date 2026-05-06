using PhotoVault.Core.Domain;

namespace PhotoVault.Core.Interfaces;

public interface ITagRepository
{
    /// Get or create a tag by name; returns the persisted tag.
    Task<Tag> UpsertTagAsync(string name, TagCategory category, bool isAI,
                              CancellationToken ct = default);

    /// Attach a tag to a media item (upsert — safe to call twice).
    Task AddMediaTagAsync(string mediaId, string tagId, double confidence,
                          TagSource source, CancellationToken ct = default);

    /// All tags attached to a media item.
    Task<IReadOnlyList<Tag>> GetTagsForMediaAsync(string mediaId,
                                                   CancellationToken ct = default);

    /// Persist (or update) the AI-generated caption for a media item.
    Task SaveCaptionAsync(string mediaId, string caption, string modelUsed,
                          CancellationToken ct = default);

    /// Retrieve the stored caption for a media item (null if none).
    Task<string?> GetCaptionAsync(string mediaId, CancellationToken ct = default);
}
