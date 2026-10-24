namespace TelegramControlStudio.Core.Security;

public static class SessionPathPolicy
{
    public static string GetAccountSessionDirectory(long accountId, string? localAppData = null)
    {
        if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
        var root = localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("Local application data directory is unavailable.");
        return Path.Combine(root, "TelegramControlStudio", "sessions", accountId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public static bool IsWithinSessionRoot(string path, string? localAppData = null)
    {
        var root = Path.GetFullPath(Path.Combine(localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelegramControlStudio", "sessions"));
        var candidate = Path.GetFullPath(path);
        return candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

