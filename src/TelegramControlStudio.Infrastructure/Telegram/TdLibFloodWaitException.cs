namespace TelegramControlStudio.Infrastructure.Telegram;

public sealed class TdLibFloodWaitException(int seconds) : Exception($"FLOOD_WAIT_{seconds}")
{
    public int Seconds { get; } = Math.Max(1, seconds);
}
