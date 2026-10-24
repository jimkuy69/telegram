using TelegramControlStudio.Core.Domain;

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
        IReadOnlyCollection<Contact> recipients)
    {
        if (activeAccount is null)
            return JobValidationResult.Invalid("ACTIVE_ACCOUNT_REQUIRED", "ต้องเลือกบัญชีที่ใช้งานก่อนสร้าง Job");

        var eligible = recipients.Count(contact => contact.AccountId == activeAccount.Id &&
                                                   contact.HasConsent &&
                                                   !contact.IsOptedOut);
        return eligible == 0
            ? JobValidationResult.Invalid("CONSENT_REQUIRED", "ไม่พบผู้รับที่มี Consent และไม่อยู่ใน Opt-out List")
            : JobValidationResult.Valid();
    }
}
