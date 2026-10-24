using System.Text.RegularExpressions;

namespace TelegramControlStudio.Core.Operations;

public static partial class FloodWaitParser
{
    public static TimeSpan? TryParse(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return null;

        var match = Pattern().Match(error);
        return match.Success && int.TryParse(match.Groups["seconds"].Value, out var seconds) && seconds >= 0
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    [GeneratedRegex(@"(?i)FLOOD_WAIT_(?<seconds>[0-9]+)")]
    private static partial Regex Pattern();
}
