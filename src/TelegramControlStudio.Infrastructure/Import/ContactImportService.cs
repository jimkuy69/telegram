using TelegramControlStudio.Core.Import;
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
        foreach (var contact in preview.Valid)
            await repository.UpsertAsync(contact, cancellationToken);
        return new(preview.Valid.Count, 0, preview.Errors);
    }
}

