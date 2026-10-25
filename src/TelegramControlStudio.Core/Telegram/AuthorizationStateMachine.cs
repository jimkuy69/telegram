namespace TelegramControlStudio.Core.Telegram;

public sealed class AuthorizationStateMachine
{
    public AuthorizationState Current { get; private set; } = AuthorizationState.Unknown;
    public event EventHandler<AuthorizationState>? StateChanged;

    public bool CanSubmitPhoneNumber => Current == AuthorizationState.WaitPhoneNumber;
    public bool CanSubmitCode => Current == AuthorizationState.WaitCode;
    public bool CanSubmitPassword => Current == AuthorizationState.WaitPassword;
    public bool IsReady => Current == AuthorizationState.Ready;

    public string UserFacingStep => Current switch
    {
        AuthorizationState.WaitTdlibParameters => "กำลังเตรียมการเชื่อมต่อ",
        AuthorizationState.WaitPhoneNumber => "กรอกเบอร์โทรศัพท์",
        AuthorizationState.WaitCode => "กรอกรหัสยืนยันจาก Telegram",
        AuthorizationState.WaitPassword => "กรอกรหัสผ่าน Two-Step Verification",
        AuthorizationState.Ready => "เชื่อมต่อแล้ว",
        AuthorizationState.LoggingOut => "กำลังออกจากระบบ",
        AuthorizationState.Closed => "ปิด Session แล้ว",
        AuthorizationState.Expired => "Session หมดอายุ",
        AuthorizationState.Error => "เกิดข้อผิดพลาด",
        _ => "รอสถานะจาก TDLib"
    };

    public void Apply(string tdlibState)
    {
        ArgumentException.ThrowIfNullOrEmpty(tdlibState);
        Current = tdlibState switch
        {
            "authorizationStateWaitTdlibParameters" => AuthorizationState.WaitTdlibParameters,
            "authorizationStateWaitPhoneNumber" => AuthorizationState.WaitPhoneNumber,
            "authorizationStateWaitCode" => AuthorizationState.WaitCode,
            "authorizationStateWaitPassword" => AuthorizationState.WaitPassword,
            "authorizationStateReady" => AuthorizationState.Ready,
            "authorizationStateLoggingOut" => AuthorizationState.LoggingOut,
            "authorizationStateClosed" => AuthorizationState.Closed,
            _ => AuthorizationState.Unknown
        };
        StateChanged?.Invoke(this, Current);
    }

    public void MarkExpired()
    {
        Current = AuthorizationState.Expired;
        StateChanged?.Invoke(this, Current);
    }

    public void MarkError()
    {
        Current = AuthorizationState.Error;
        StateChanged?.Invoke(this, Current);
    }
}

