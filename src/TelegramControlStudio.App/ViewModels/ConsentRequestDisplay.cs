using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Safety;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.App.ViewModels;

/// <summary>
/// Turns the reason codes the safety policies return into the short Thai labels the operator sees.
/// The codes themselves stay in the evidence rows so the audit trail keeps the machine-readable form.
/// </summary>
public static class ConsentRequestDisplay
{
    public static string DescribeReason(string? reasonCode) => reasonCode switch
    {
        ConsentRequestPolicy.AllowedReason => "ถามได้",
        ConsentRequestPolicy.NoOwnNumberReason => "ไม่มีเบอร์โทรในสมุดรายชื่อ",
        ConsentRequestPolicy.AlreadyConsentedReason => "มี Consent แล้ว",
        ConsentRequestPolicy.OptedOutReason => "สั่งหยุดรับข้อความ (Opt-out)",
        ConsentRequestPolicy.DeclinedReason => "เคยตอบปฏิเสธคำขอไปแล้ว",
        ConsentRequestPolicy.CooldownReason => "ยังอยู่ในระยะพักการถาม",
        ConsentRequestPolicy.DailyLimitReason => "เต็มโควตาประจำวันแล้ว",
        ConsentRequestQueueService.UnknownContactReason => "ไม่พบผู้ติดต่อนี้",
        SendSafetyPolicy.KillSwitchReason => "หยุดฉุกเฉินอยู่",
        SendSafetyPolicy.TestModeReason => "อยู่นอก allowlist ของโหมดทดสอบ",
        _ => string.IsNullOrWhiteSpace(reasonCode) ? "-" : reasonCode
    };

    public static string DescribeEventKind(string kind) => kind switch
    {
        ConsentRequestKinds.Requested => "ถามไป",
        ConsentRequestKinds.Granted => "ได้ Consent",
        ConsentRequestKinds.Rejected => "ปฏิเสธ",
        ConsentRequestKinds.OptedOut => "Opt-out",
        _ => kind,
    };

    public static string DescribeSource(string source) => source switch
    {
        ConsentRequestSources.JobRunner => "ระบบส่งคำขออัตโนมัติ",
        ConsentRequestSources.TelegramMessage => "อ่านจากคำตอบในแชท",
        ConsentRequestSources.OperatorInterface => "ผู้ปฏิบัติงานบันทึกเอง",
        _ => string.IsNullOrWhiteSpace(source) ? "-" : source,
    };

    /// <summary>The line shown in the contacts grid so the operator can see who is still waiting.</summary>
    public static string DescribeLatestEvent(string? kind, DateTimeOffset? occurredAt)
    {
        if (kind is null || occurredAt is null) return "ยังไม่ได้ถาม";
        return $"{DescribeEventKind(kind)} • {occurredAt.Value.ToLocalTime():dd/MM/yyyy}";
    }
}
