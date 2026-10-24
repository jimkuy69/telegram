namespace TelegramControlStudio.Core.Domain;

public sealed record MediaFile(
    long Id,
    long AccountId,
    string FileName,
    string LocalPath,
    string Sha256,
    string MimeType,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt);

