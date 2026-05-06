namespace PhotoVault.Core.Pipeline;

/// <summary>
/// Represents a single media file queued for background processing.
/// Stages run in order; mutable properties carry intermediate results
/// between stages (e.g. BlurScore from BlurCheck → DupeCheck).
/// </summary>
public class MediaProcessingJob
{
    public string           MediaId      { get; init; } = default!;
    public string           AbsolutePath { get; init; } = default!;
    public ProcessingStage[] Stages      { get; init; } = DefaultStages;

    // Scratch-pad — written by BlurCheck, read by DupeCheck
    public double BlurScore { get; set; }
    public bool   IsBlurry  { get; set; }

    // Default full pipeline order
    public static readonly ProcessingStage[] DefaultStages =
    [
        ProcessingStage.Metadata,
        ProcessingStage.BlurCheck,
        ProcessingStage.DupeCheck,
        ProcessingStage.Thumbnail,
        ProcessingStage.Tagging,
        ProcessingStage.GraphIndex,   // runs after tagging so tags + GPS are available
    ];

    public static MediaProcessingJob Create(string mediaId, string absolutePath,
                                             ProcessingStage[]? stages = null)
        => new() { MediaId = mediaId, AbsolutePath = absolutePath,
                   Stages  = stages ?? DefaultStages };
}

public enum ProcessingStage
{
    Thumbnail,
    Metadata,
    DupeCheck,
    BlurCheck,
    Tagging,
    GraphIndex,     // build/update knowledge graph after tagging
    FaceDetection,
    Embedding
}
