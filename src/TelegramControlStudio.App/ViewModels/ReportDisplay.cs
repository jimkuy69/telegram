using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Operations;
using TelegramControlStudio.Core.Reporting;
using TelegramControlStudio.Core.Safety;
using TelegramControlStudio.Infrastructure.Persistence;

namespace TelegramControlStudio.App.ViewModels;

/// <summary>
/// Thai labels for the result report. The raw reason code is always kept next to the label so an
/// operator can quote the machine-readable value when reporting a problem.
/// </summary>
public static class ReportDisplay
{
    public static string DescribeItemStatus(string status) => status switch
    {
        "Completed" => "ส่งสำเร็จ",
        "Failed" => "ล้มเหลว",
        "Skipped" => "ข้าม",
        "Cancelled" => "ยกเลิก",
        "WaitingForFloodWait" => "รอ Flood wait",
        "WaitingForConfirmation" => "รอยืนยัน",
        "Queued" => "เข้าคิว",
        "Running" => "กำลังส่ง",
        "Paused" => "พักไว้",
        _ => status,
    };

    public static string DescribeFailureKind(JobFailureKind kind) => kind switch
    {
        JobFailureKind.None => "ไม่จัดประเภท",
        JobFailureKind.RetryableNetwork => "เครือข่าย (ลองใหม่ได้)",
        JobFailureKind.FloodWait => "Telegram ให้รอ (Flood wait)",
        JobFailureKind.PermissionDenied => "สิทธิ์ไม่พอ",
        JobFailureKind.ConsentDenied => "ผู้รับไม่ให้ Consent",
        JobFailureKind.SessionExpired => "เซสชันบัญชีหมดอายุ",
        _ => "ต้องแก้ถาวร",
    };

    public static string DescribeReason(string? reason) => reason switch
    {
        ResultReportMath.UnspecifiedReason => "ไม่ระบุเหตุผล",
        SendSafetyPolicy.ConsentReason => "ผู้รับยังไม่มี Consent หรือ Opt-out แล้ว",
        "TDLIB_MESSAGE_SEND_FAILED" => "Telegram ไม่ยอมรับการส่งข้อความ",
        "Cancelled by user" => "ผู้ใช้ยกเลิกเอง",
        "QUEUE_RUN_FAILED" => "ตัวรันคิวทำงานผิดพลาด",
        "AUTO_RUN_FAILED" => "ตัวรันอัตโนมัติทำงานผิดพลาด",
        _ => ConsentRequestDisplay.DescribeReason(reason),
    };

    public static string DescribeConsentKind(string kind) => kind switch
    {
        ConsentRequestKinds.Requested => "ถามไป",
        ConsentRequestKinds.Granted => "ได้ Consent",
        ConsentRequestKinds.Rejected => "ตอบปฏิเสธ",
        ConsentRequestKinds.OptedOut => "สั่ง Opt-out",
        _ => ConsentRequestDisplay.DescribeEventKind(kind),
    };

    public static string Percent(double rate) => $"{rate * 100:0.0}%";
}
