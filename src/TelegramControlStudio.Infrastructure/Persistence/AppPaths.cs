namespace TelegramControlStudio.Infrastructure.Persistence;

public static class AppPaths
{
    public const string DatabasePathOverrideVariable = "TELEGRAM_CONTROL_STUDIO_DB";

    public static string ResolveDatabasePath()
    {
        var overridePath = Environment.GetEnvironmentVariable(DatabasePathOverrideVariable);
        return !string.IsNullOrWhiteSpace(overridePath)
            ? Path.GetFullPath(overridePath.Trim())
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TelegramControlStudio",
                "telegram-control-studio.db");
    }
}
