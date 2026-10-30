using System.Globalization;
using System.Text;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Safety;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Safety;

namespace TelegramControlStudio.Infrastructure.Telegram;

/// <summary>
/// Executes bot chat commands for invited users. Owners can inspect and steer jobs; friends can only manage their own consent.
/// </summary>
public sealed class BotCommandRouter(string databasePath)
{
    private const string FallbackPublicBaseUrl = "https://tcs.bn9.one";

    public async Task<string> HandleAsync(BotMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var text = message.Text.Trim();
        if (text.Length == 0) return string.Empty;

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].Split('@')[0].ToLowerInvariant();
        var argument = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        var users = new AppUserRepository(databasePath);
        var user = await users.GetAsync(message.FromId, cancellationToken);
        if (user is null)
            return $"""
                บัญชีนี้ยังไม่ได้รับเชิญ
                Telegram User ID: {message.FromId}
                ส่ง ID นี้ให้เจ้าของระบบเพื่อขอ invite
                """;
        if (user.IsBlocked)
            return "บัญชีนี้ถูกระงับการใช้งาน";

        if (user.Role == AppUserRole.Friend)
            return await HandleFriendAsync(user, command, argument, cancellationToken);
        return await HandleOwnerAsync(user, command, argument, cancellationToken);
    }

    private async Task<string> HandleFriendAsync(AppUser user, string command, string argument, CancellationToken cancellationToken)
    {
        switch (command)
        {
            case "/start":
            case "/help":
                return """
                    Telegram Control Studio (เพื่อน)
                    /me — ดูสถานะความยินยอมของคุณ
                    /optin — ให้ความยินยอมรับข้อความ
                    /optout — ถอนความยินยอม (หยุดรับข้อความ)
                    """;
            case "/me":
                return await DescribeSelfAsync(user, cancellationToken);
            case "/optin":
                return await SetSelfConsentAsync(user, consent: true, optOut: false, cancellationToken);
            case "/optout":
                return await SetSelfConsentAsync(user, consent: false, optOut: true, cancellationToken);
            default:
                return "คำสั่งนี้ใช้ได้เฉพาะเจ้าของระบบ — พิมพ์ /help เพื่อดูคำสั่งของคุณ";
        }
    }

    private async Task<string> HandleOwnerAsync(AppUser user, string command, string argument, CancellationToken cancellationToken)
    {
        switch (command)
        {
            case "/start":
            case "/help":
                return """
                    Telegram Control Studio (เจ้าของ)
                    /status — สรุปสถานะระบบและคิวงาน
                    /jobs — งานล่าสุด 10 รายการ
                    /job <id> — รายละเอียดงาน
                    /approve <id> — ยืนยันคิวงานที่รออนุมัติ
                    /cancel <id> — ยกเลิกงานที่รอดำเนินการ
                    /testmode on|off — เปิด/ปิดโหมดทดสอบ
                    /kill on|off — เปิด/ปลดหยุดฉุกเฉิน
                    /invite — สร้างรหัสเชิญเพื่อน
                    /users — รายชื่อผู้ใช้เว็บ
                    /me — ดูสถานะความยินยอมของคุณเอง
                    /whoami — ข้อมูลบัญชีคุณ
                    """;
            case "/whoami":
                return $"คุณคือ {user.DisplayName} (id {user.TelegramUserId})\nบทบาท: {user.Role}";
            case "/status":
                return await DescribeStatusAsync(cancellationToken);
            case "/jobs":
                return await DescribeJobsAsync(cancellationToken);
            case "/job":
                return await DescribeJobAsync(argument, cancellationToken);
            case "/approve":
                return await ApproveAsync(user, argument, cancellationToken);
            case "/cancel":
                return await CancelAsync(user, argument, cancellationToken);
            case "/testmode":
                return await SetTestModeAsync(argument, cancellationToken);
            case "/kill":
                return await SetKillSwitchAsync(argument, cancellationToken);
            case "/invite":
                return await CreateInviteAsync(user, cancellationToken);
            case "/users":
                return await DescribeUsersAsync(cancellationToken);
            case "/me":
                return await DescribeSelfAsync(user, cancellationToken);
            default:
                return "ไม่รู้จักคำสั่งนี้ — พิมพ์ /help";
        }
    }

    private async Task<string> DescribeStatusAsync(CancellationToken cancellationToken)
    {
        var account = await FirstAccountAsync(cancellationToken);
        if (account is null) return "ยังไม่มีบัญชี Telegram ในเครื่องนี้";

        var counts = await new JobRepository(databasePath).CountByStatusAsync(account.Id, cancellationToken);
        var safety = await new SendSafetyRepository(databasePath).GetAsync(cancellationToken);
        var builder = new StringBuilder();
        builder.AppendLine($"บัญชี: {account.DisplayName} ({account.Status})");
        builder.AppendLine($"โหมดทดสอบ: {(safety.TestModeEnabled ? "เปิด" : "ปิด")} | หยุดฉุกเฉิน: {(safety.KillSwitchEngaged ? "เปิด" : "ปิด")} | allowlist: {safety.AllowlistUserIds.Count}");
        builder.AppendLine("คิวงาน:");
        if (counts.Count == 0) builder.AppendLine("• ไม่มีงาน");
        foreach (var pair in counts.OrderBy(pair => pair.Key))
            builder.AppendLine($"• {pair.Key}: {pair.Value}");
        return builder.ToString().TrimEnd();
    }

    private async Task<string> DescribeJobsAsync(CancellationToken cancellationToken)
    {
        var account = await FirstAccountAsync(cancellationToken);
        if (account is null) return "ยังไม่มีบัญชี Telegram ในเครื่องนี้";
        var jobs = await new JobRepository(databasePath).ListAsync(account.Id, cancellationToken);
        if (jobs.Count == 0) return "ยังไม่มีงาน";

        var builder = new StringBuilder("งานล่าสุด:\n");
        foreach (var job in jobs.Take(10))
            builder.AppendLine($"{Short(job.Id)}  {job.Status}  ผู้รับ {job.TargetCount}  {job.CreatedAt.ToLocalTime():dd/MM HH:mm}");
        builder.AppendLine("\nดูรายละเอียด: /job <id>");
        return builder.ToString().TrimEnd();
    }

    private async Task<string> DescribeJobAsync(string prefix, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return "ใช้: /job <id>";
        var job = await ResolveJobAsync(prefix, cancellationToken);
        if (job is null) return "ไม่พบงาน (หรือ id ไม่ชัดเจน)";
        var items = await new JobItemRepository(databasePath).ListAsync(job.Id, cancellationToken);
        var builder = new StringBuilder();
        builder.AppendLine($"งาน {job.Id}");
        builder.AppendLine($"สถานะ: {job.Status} | ผู้รับ {job.TargetCount} | สร้าง {job.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm}");
        if (items.Count > 0)
        {
            builder.AppendLine("รายการ:");
            foreach (var group in items.GroupBy(item => item.Status).OrderBy(group => group.Key))
                builder.AppendLine($"• {group.Key}: {group.Count()}");
        }
        if (!string.IsNullOrWhiteSpace(job.MessageText))
            builder.AppendLine($"ข้อความ: {Truncate(job.MessageText, 120)}");
        if (!string.IsNullOrWhiteSpace(job.LastError))
            builder.AppendLine($"ข้อผิดพลาดล่าสุด: {Truncate(job.LastError, 200)}");
        return builder.ToString().TrimEnd();
    }

    private async Task<string> ApproveAsync(AppUser user, string prefix, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return "ใช้: /approve <id>";
        var job = await ResolveJobAsync(prefix, cancellationToken);
        if (job is null) return "ไม่พบงาน (หรือ id ไม่ชัดเจน)";
        if (job.Status != JobStatus.WaitingForConfirmation)
            return $"งานนี้อยู่ในสถานะ {job.Status} — อนุมัติได้เฉพาะงานที่รออนุมัติ";

        var items = new JobItemRepository(databasePath);
        var moved = await items.UpdateStatusForJobAsync(job.Id, JobStatus.WaitingForConfirmation.ToString(), JobStatus.Queued.ToString(), cancellationToken);
        await new JobRepository(databasePath).UpdateStatusAsync(job.Id, JobStatus.Queued, cancellationToken: cancellationToken);
        await LogAsync(user, job, "JobApprovedFromBot", $"Job {job.Id} approved from bot by {user.TelegramUserId} ({moved} items queued).", cancellationToken);
        return $"อนุมัติแล้ว: {Short(job.Id)} → Queued ({moved} รายการ)\nการส่งจริงเริ่มจากแอปเดสก์ท็อป";
    }

    private async Task<string> CancelAsync(AppUser user, string prefix, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return "ใช้: /cancel <id>";
        var job = await ResolveJobAsync(prefix, cancellationToken);
        if (job is null) return "ไม่พบงาน (หรือ id ไม่ชัดเจน)";
        if (job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.PartiallyCompleted or JobStatus.Cancelled)
            return $"งานนี้จบแล้ว ({job.Status}) ยกเลิกไม่ได้";

        var cancelled = await new JobItemRepository(databasePath).CancelPendingAsync(job.Id, cancellationToken);
        await new JobRepository(databasePath).UpdateStatusAsync(job.Id, JobStatus.Cancelled, cancellationToken: cancellationToken);
        await LogAsync(user, job, "JobCancelledFromBot", $"Job {job.Id} cancelled from bot by {user.TelegramUserId} ({cancelled} pending items cancelled).", cancellationToken);
        return $"ยกเลิกแล้ว: {Short(job.Id)} ({cancelled} รายการที่รอดำเนินการ)";
    }

    private async Task<string> SetTestModeAsync(string argument, CancellationToken cancellationToken)
    {
        var enabled = ParseToggle(argument);
        if (enabled is null) return "ใช้: /testmode on|off";
        await new SendSafetyRepository(databasePath).SetTestModeAsync(enabled.Value);
        return $"โหมดทดสอบ: {(enabled.Value ? "เปิด" : "ปิด")}";
    }

    private async Task<string> SetKillSwitchAsync(string argument, CancellationToken cancellationToken)
    {
        var engaged = ParseToggle(argument);
        if (engaged is null) return "ใช้: /kill on|off";
        await new SendSafetyRepository(databasePath).SetKillSwitchAsync(engaged.Value);
        if (engaged.Value) EmergencyStopSignal.Trigger();
        return $"หยุดฉุกเฉิน: {(engaged.Value ? "เปิด — ระบบบล็อกการส่งทั้งหมด" : "ปิดแล้ว")}";
    }

    private async Task<string> CreateInviteAsync(AppUser user, CancellationToken cancellationToken)
    {
        var invite = await new InviteRepository(databasePath).CreateAsync(
            AppUserRole.Friend,
            $"created by bot ({user.TelegramUserId})",
            DateTimeOffset.UtcNow.AddDays(7),
            user.TelegramUserId,
            cancellationToken);
        var baseUrl = await new AppSettingsRepository(databasePath).GetAsync("public_base_url", cancellationToken);
        var root = string.IsNullOrWhiteSpace(baseUrl) ? FallbackPublicBaseUrl : baseUrl.TrimEnd('/');
        return $"""
            สร้าง invite แล้ว (ใช้ได้ 7 วัน, ใช้ได้ครั้งเดียว)
            รหัส: {invite.Code}
            ลิงก์: {root}/invite/{invite.Code}
            """;
    }

    private async Task<string> DescribeUsersAsync(CancellationToken cancellationToken)
    {
        var users = await new AppUserRepository(databasePath).ListAsync(cancellationToken);
        var builder = new StringBuilder($"ผู้ใช้เว็บ: {users.Count} คน\n");
        foreach (var user in users.Take(20))
            builder.AppendLine($"• {user.DisplayName} ({user.TelegramUserId}) {user.Role}{(user.IsBlocked ? " — ระงับ" : "")}");
        return builder.ToString().TrimEnd();
    }

    private async Task<string> DescribeSelfAsync(AppUser user, CancellationToken cancellationToken)
    {
        var account = await FirstAccountAsync(cancellationToken);
        if (account is null) return "ยังไม่มีบัญชี Telegram ในเครื่องนี้";
        var contact = (await new ContactRepository(databasePath).ListAsync(account.Id, cancellationToken))
            .FirstOrDefault(item => item.TelegramUserId == user.TelegramUserId);
        if (contact is null) return "ไม่พบคุณในรายชื่อผู้รับของบัญชีนี้";
        var consent = contact.IsOptedOut ? "ถอนความยินยอม (opt-out)" : contact.HasConsent ? "ให้ความยินยอมแล้ว" : "ยังไม่ให้ความยินยอม";
        return $"""
            {contact.FirstName} {contact.LastName}
            สถานะ: {consent}
            อัปเดต: {contact.UpdatedAt.ToLocalTime():dd/MM/yyyy HH:mm}
            """;
    }

    private async Task<string> SetSelfConsentAsync(AppUser user, bool consent, bool optOut, CancellationToken cancellationToken)
    {
        var account = await FirstAccountAsync(cancellationToken);
        if (account is null) return "ยังไม่มีบัญชี Telegram ในเครื่องนี้";
        var contacts = new ContactRepository(databasePath);
        var contact = (await contacts.ListAsync(account.Id, cancellationToken))
            .FirstOrDefault(item => item.TelegramUserId == user.TelegramUserId);
        if (contact is null) return "ไม่พบคุณในรายชื่อผู้รับของบัญชีนี้";

        await contacts.SetOptOutAsync(account.Id, user.TelegramUserId, optOut, cancellationToken);
        await contacts.SetConsentAsync(account.Id, user.TelegramUserId, consent, cancellationToken);
        await new ActivityLogRepository(databasePath).AppendAsync(new ActivityLog(
            account.Id, null, "Info", optOut ? "ContactOptedOutFromBot" : "ContactConsentFromBot",
            $"User {user.TelegramUserId} {(optOut ? "opted out" : "consented")} via bot.", DateTimeOffset.UtcNow), cancellationToken);
        return optOut ? "บันทึกแล้ว: คุณจะไม่ได้รับข้อความจากระบบนี้" : "บันทึกแล้ว: คุณให้ความยินยอมรับข้อความ";
    }

    private async Task<Job?> ResolveJobAsync(string prefix, CancellationToken cancellationToken)
    {
        var account = await FirstAccountAsync(cancellationToken);
        if (account is null) return null;
        var matches = (await new JobRepository(databasePath).ListAsync(account.Id, cancellationToken))
            .Where(job => job.Id.ToString().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private async Task<Account?> FirstAccountAsync(CancellationToken cancellationToken) =>
        (await new AccountRepository(databasePath).ListAsync(cancellationToken)).FirstOrDefault();

    private async Task LogAsync(AppUser user, Job job, string eventType, string message, CancellationToken cancellationToken) =>
        await new ActivityLogRepository(databasePath).AppendAsync(new ActivityLog(
            job.AccountId, job.Id, "Info", eventType, message, DateTimeOffset.UtcNow), cancellationToken);

    private static bool? ParseToggle(string argument) => argument.Trim().ToLowerInvariant() switch
    {
        "on" or "true" or "1" => true,
        "off" or "false" or "0" => false,
        _ => null
    };

    private static string Short(Guid id) => id.ToString()[..8];

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
