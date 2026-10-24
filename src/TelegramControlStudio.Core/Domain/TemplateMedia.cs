namespace TelegramControlStudio.Core.Domain;

public sealed record TemplateMedia(long TemplateId, long MediaFileId, long AccountId, int SortOrder);

