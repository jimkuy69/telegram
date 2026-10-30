namespace TelegramControlStudio.Infrastructure.Persistence;

/// <summary>Keys and parser for the web-first sending toggle shared by the web runner, web UI and tests.</summary>
public static class WebSendSettings
{
    public const string EnabledKey = "web_sending_enabled";
    public const string LastActivityKey = "web_runner_last_activity";

    public static bool IsEnabled(string? value) => value is "1" or "true";
}
