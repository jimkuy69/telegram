using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Import;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.Infrastructure.Telegram;

public sealed record ContactPhoneImportResult(
    IReadOnlyList<Contact> Resolved,
    IReadOnlyList<string> Unresolved,
    IReadOnlyList<string> Errors,
    int BatchesSent);

/// <summary>
/// Turns a phone-only contact list into Telegram user ids through TDLib <c>importContacts</c>, the only
/// place that knows which number belongs to which account. The call writes to the signed-in account's
/// Telegram contact list, so it never runs without an explicit confirmation from the operator.
/// </summary>
public sealed class TdLibContactPhoneImportService(string databasePath)
{
    // Telegram's contacts.importContacts takes at most 200 numbers per request.
    public const int MaxContactsPerRequest = 200;

    public async Task<ContactPhoneImportResult> ImportAsync(
        ITdLibClient client,
        long accountId,
        IReadOnlyList<PhoneOnlyContact> phoneOnly,
        bool confirmed,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(phoneOnly);
        if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
        if (!confirmed) return new([], [], ["CONFIRMATION_REQUIRED"], 0);
        if (phoneOnly.Count == 0) return new([], [], [], 0);

        var resolved = new List<Contact>();
        var unresolved = new List<string>();
        var errors = new List<string>();
        var total = phoneOnly.Count;
        var batchesSent = 0;
        var done = 0;
        var position = 0;
        progress?.Report((done, total));
        while (position < total)
        {
            var batch = new List<PhoneOnlyContact>(MaxContactsPerRequest);
            for (; position < total && batch.Count < MaxContactsPerRequest; position++)
                batch.Add(phoneOnly[position]);
            var outcome = await SendAsync(client, batch, cancellationToken);
            batchesSent++;
            done += batch.Count;
            progress?.Report((done, total));
            if (outcome.Error is not null)
            {
                errors.Add($"หยุดที่คำขอที่ {batchesSent} ({batch.Count} เบอร์): {outcome.Error}");
                // position already moved past this batch, so the rows that never got their answer start
                // with the batch itself.
                unresolved.AddRange(phoneOnly.Skip(position - batch.Count).Select(missing => $"{missing.PhoneNumber}: ไม่ได้ส่ง เพราะหยุดตั้งแต่คำขอที่ {batchesSent}"));
                break;
            }
            var mapper = Enumerable.Range(0, Math.Min(batch.Count, outcome.UserIds.Count)).FirstOrDefault(index => outcome.UserIds[index] > 0, -1);
            if (mapper >= 0)
            {
                var mismatch = await VerifyAsync(client, batch[mapper], outcome.UserIds[mapper], cancellationToken);
                if (mismatch is not null)
                {
                    errors.Add($"หยุดที่คำขอที่ {batchesSent} ({batch.Count} เบอร์) เพราะตรวจแล้ว id ไม่ตรงกับเบอร์: {mismatch}");
                    unresolved.AddRange(phoneOnly.Skip(position - batch.Count).Select(missing => $"{missing.PhoneNumber}: ไม่ได้บันทึก เพราะตรวจ id ของชุดนี้ไม่ผ่าน"));
                    break;
                }
            }
            for (var index = 0; index < batch.Count; index++)
            {
                var userId = index < outcome.UserIds.Count ? outcome.UserIds[index] : 0;
                if (userId > 0)
                    resolved.Add(ToContact(accountId, batch[index], userId));
                else
                    unresolved.Add($"{batch[index].PhoneNumber}: Telegram ไม่พบบัญชีที่ลงทะเบียนด้วยเบอร์นี้ หรือไม่ให้ค้นหาด้วยเบอร์");
            }
        }

        if (resolved.Count > 0)
            await new ContactRepository(databasePath).UpsertManyAsync(resolved, cancellationToken);
        await new ActivityLogRepository(databasePath).AppendAsync(new ActivityLog(
            accountId,
            null,
            "Info",
            "ContactsResolvedByPhone",
            $"importContacts sent {batchesSent} request(s): resolved {resolved.Count}, unresolved {unresolved.Count}, errors {errors.Count}.",
            DateTimeOffset.UtcNow), cancellationToken);
        return new(resolved, unresolved, errors, batchesSent);
    }

    private static Contact ToContact(long accountId, PhoneOnlyContact row, long telegramUserId) =>
        new(accountId, telegramUserId, row.FirstName, row.LastName, row.Username, row.PhoneNumber,
            row.HasConsent, row.IsOptedOut, DateTimeOffset.UtcNow);

    /// <summary>
    /// TDLib answers by position, so the pairing is already fact; reading one user back and comparing its
    /// phone catches a reply that was reordered or truncated before the whole batch is stored against
    /// the wrong people.
    /// </summary>
    private static async Task<string?> VerifyAsync(ITdLibClient client, PhoneOnlyContact row, long userId, CancellationToken cancellationToken)
    {
        string response;
        try
        {
            response = await client.SendAsync("getUser", new Dictionary<string, object?> { ["user_id"] = userId }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return $"อ่าน getUser({userId}) ไม่ได้: {exception.Message}";
        }
        if (TdLibResponseGuard.TryGetError(response, out var error))
            return $"getUser({userId}) ถูกปฏิเสธ: {error}";
        string? reported = null;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(response);
            if (document.RootElement.TryGetProperty("phone_number", out var phone) && phone.ValueKind == System.Text.Json.JsonValueKind.String)
                reported = phone.GetString();
        }
        catch (System.Text.Json.JsonException)
        {
            return $"getUser({userId}) ตอบกลับเป็น JSON ที่อ่านไม่ได้";
        }
        return SamePhone(reported, row.PhoneNumber)
            ? null
            : $"{row.PhoneNumber} ได้ id {userId} ที่ TDLib รายงานว่าเป็นเบอร์ {reported ?? "(ว่าง)"}";
    }

    private static bool SamePhone(string? left, string right) =>
        left is not null &&
        string.Equals(Digits(left), Digits(right), StringComparison.Ordinal);

    private static string Digits(string phone) => new(phone.Where(char.IsDigit).ToArray());

    private static async Task<ImportedContactsResult> SendAsync(ITdLibClient client, IReadOnlyList<PhoneOnlyContact> batch, CancellationToken cancellationToken)
    {
        var payload = new List<Dictionary<string, object?>>(batch.Count);
        foreach (var row in batch)
            payload.Add(new Dictionary<string, object?>
            {
                ["@type"] = "contact",
                ["user_id"] = 0L,
                ["phone_number"] = row.PhoneNumber,
                ["first_name"] = row.FirstName,
                ["last_name"] = row.LastName
            });
        try
        {
            var response = await client.SendAsync("importContacts",
                new Dictionary<string, object?> { ["contacts"] = payload }, cancellationToken);
            return TdLibImportedContactsParser.Parse(response);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TdLibFloodWaitException flood)
        {
            return new ImportedContactsResult($"FLOOD_WAIT_{flood.Seconds} — Telegram ให้รอ {flood.Seconds} วินาที", []);
        }
        catch (Exception exception)
        {
            return new ImportedContactsResult($"importContacts ล้มเหลว: {exception.Message}", []);
        }
    }
}
