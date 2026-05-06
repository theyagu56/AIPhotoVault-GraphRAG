namespace PhotoVault.Core.Domain;

public record Thumbnail(string Id, string MediaId, ThumbnailSize Size,
                        string Path, int Width, int Height, DateTime CreatedAt);

public enum ThumbnailSize { sm, md, lg }
