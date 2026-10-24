namespace TelegramControlStudio.Core.Import;

public sealed record MediaValidationResult(bool IsValid, string? ErrorCode);

public static class MediaFileValidator
{
    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    { "image/jpeg", "image/png", "image/webp", "application/pdf" };

    public static MediaValidationResult Validate(string path, string mimeType, long sizeBytes, string allowedRoot, long maxBytes = 10 * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        ArgumentException.ThrowIfNullOrWhiteSpace(allowedRoot);
        if (sizeBytes < 0 || sizeBytes > maxBytes) return new(false, "FILE_SIZE_INVALID");
        if (!AllowedMimeTypes.Contains(mimeType)) return new(false, "MIME_TYPE_NOT_ALLOWED");
        var root = Path.GetFullPath(allowedRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? new(true, null)
            : new(false, "PATH_OUTSIDE_ALLOWED_ROOT");
    }
}

