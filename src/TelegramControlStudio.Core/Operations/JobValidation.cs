using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Safety;

namespace TelegramControlStudio.Core.Operations;

public sealed record JobValidationResult(bool IsValid, string? ErrorCode, string? Message)
{
    public static JobValidationResult Valid() => new(true, null, null);
    public static JobValidationResult Invalid(string code, string message) => new(false, code, message);
}

public static class JobValidation
{
    public static JobValidationResult Validate(
        Account? activeAccount,
        IReadOnlyCollection<Contact> recipients,
        SendPurpose purpose = SendPurpose.Campaign)
    {
        if (activeAccount is null)
            return JobValidationResult.Invalid("ACTIVE_ACCOUNT_REQUIRED", "ต้องเลือกบัญชีที่ใช้งานก่อนสร้าง Job");

        // The two purposes need opposite recipients: a campaign needs people who already said yes,
        // an opt-in request needs people who have not been asked yet and whose number we hold.
        var eligible = purpose == SendPurpose.Campaign
            ? recipients.Count(contact => contact.AccountId == activeAccount.Id &&
                                           contact.HasConsent &&
                                           !contact.IsOptedOut)
            : recipients.Count(IsRequestCandidate);
        if (eligible > 0) return JobValidationResult.Valid();
        return purpose == SendPurpose.Campaign
            ? JobValidationResult.Invalid("CONSENT_REQUIRED", "ไม่พบผู้รับที่มี Consent และไม่อยู่ใน Opt-out List")
            : JobValidationResult.Invalid(
                "CONSENT_REQUEST_ELIGIBLE_REQUIRED",
                "ไม่พบรายชื่อที่ขอความยินยอมได้ ต้องเป็นรายชื่อของบัญชีนี้ ยังไม่อนุญาต ไม่ Opt-out และมีเบอร์โทรที่เราเก็บไว้เอง");
    }

    public static bool IsRequestCandidate(Contact contact)
    {
        ArgumentNullException.ThrowIfNull(contact);
        return !contact.HasConsent && !contact.IsOptedOut && !string.IsNullOrWhiteSpace(contact.PhoneNumber);
    }
}
