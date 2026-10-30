using System.Globalization;

namespace TelegramControlStudio.Core.Safety;

public sealed record SendSafetySettings(
    bool TestModeEnabled,
    IReadOnlySet<long> AllowlistUserIds,
    bool KillSwitchEngaged,
    DateTimeOffset? UpdatedAt)
{
    public static SendSafetySettings SafeDefault { get; } = new(true, new HashSet<long>(), false, null);
}

public enum SendSafetyVerdict
{
    Allow,
    Skip,
    Block
}

public sealed record SendSafetyDecision(SendSafetyVerdict Verdict, string ReasonCode)
{
    public bool CanSend => Verdict == SendSafetyVerdict.Allow;
}

public static class SendSafetyPolicy
{
    public const string AllowedReason = "ALLOWED";
    public const string KillSwitchReason = "KILL_SWITCH_ENGAGED";
    public const string ConsentReason = "CONSENT_REQUIRED_OR_OPTED_OUT";
    public const string TestModeReason = "TEST_MODE_NOT_ALLOWLISTED";

    private static readonly char[] AllowlistSeparators = [',', ';', '|', '\r', '\n', '\t', ' '];

    public static SendSafetyDecision Evaluate(
        SendSafetySettings settings,
        long telegramUserId,
        bool hasConsent = true,
        bool isOptedOut = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.KillSwitchEngaged)
            return new(SendSafetyVerdict.Block, KillSwitchReason);
        if (!hasConsent || isOptedOut)
            return new(SendSafetyVerdict.Skip, ConsentReason);
        if (settings.TestModeEnabled && !settings.AllowlistUserIds.Contains(telegramUserId))
            return new(SendSafetyVerdict.Skip, TestModeReason);
        return new(SendSafetyVerdict.Allow, AllowedReason);
    }

    public static IReadOnlySet<long> ParseAllowlist(string? raw)
    {
        var result = new HashSet<long>();
        if (string.IsNullOrWhiteSpace(raw)) return result;
        foreach (var token in raw.Split(AllowlistSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (long.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0)
                result.Add(id);
        }
        return result;
    }

    public static string FormatAllowlist(IEnumerable<long> userIds)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        return string.Join(Environment.NewLine, userIds.Where(id => id > 0).Distinct().Order());
    }

    public static string Describe(SendSafetySettings settings, int allowlistCount)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.KillSwitchEngaged) return "หยุดฉุกเฉินอยู่: ระบบบล็อกการส่งทั้งหมด";
        return settings.TestModeEnabled
            ? $"โหมดทดสอบ: ส่งได้เฉพาะ {allowlistCount} รายใน allowlist"
            : "โหมดใช้งานจริง: ส่งได้ทุกคนที่ให้ Consent";
    }
}
