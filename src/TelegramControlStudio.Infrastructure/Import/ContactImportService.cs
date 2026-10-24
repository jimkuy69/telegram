using TelegramControlStudio.Core.Import;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.Infrastructure.Import;

public sealed record ContactImportCommitResult(int Imported, int Skipped, IReadOnlyList<string> Errors);

public sealed class ContactImportService(string databasePath)
{
    public async Task<ContactImportCommitResult> CommitAsync(ContactImportResult preview, bool confirmed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (!confirmed) return new(0, preview.Valid.Count, [.. preview.Errors, "CONFIRMATION_REQUIRED"]);
        var repository = new ContactRepository(databasePath);
        await repository.UpsertManyAsync(preview.Valid, cancellationToken);
        if (preview.Valid.Count > 0)
        {
            await new ActivityLogRepository(databasePath).AppendAsync(new ActivityLog(
                preview.Valid[0].AccountId,
                null,
                "Info",
                "ContactsImported",
                $"Imported {preview.Valid.Count} contact(s); skipped {preview.Errors.Count} row(s).",
                DateTimeOffset.UtcNow), cancellationToken);
        }
        return new(preview.Valid.Count, 0, preview.Errors);
    }
}