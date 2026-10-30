namespace TelegramControlStudio.Core.Safety;

public sealed class SendSafetyBlockedException(string reasonCode, SendSafetyVerdict verdict)
    : InvalidOperationException(reasonCode)
{
    public string ReasonCode { get; } = reasonCode;

    public SendSafetyVerdict Verdict { get; } = verdict;
}
