namespace TelegramControlStudio.Core.Safety;

public enum ConsentReplyVerdict
{
    Unanswered,
    Granted,
    Rejected,
    OptedOut
}

/// <summary>
/// What a free-text answer means. <see cref="MatchedKeyword"/> is the only part of the message that
/// is ever stored, so the evidence explains the decision without keeping the person's words.
/// </summary>
public sealed record ConsentReplyDecision(ConsentReplyVerdict Verdict, string? MatchedKeyword)
{
    public bool ChangesConsent => Verdict is ConsentReplyVerdict.Granted or ConsentReplyVerdict.OptedOut;
}

/// <summary>
/// Reads consent answers. Ordering is the whole design: an opt-out is checked first and outranks
/// everything, a refusal is checked before an acceptance so "ไม่ยินยอม" can never be read as
/// "ยินยอม", and anything unclear stays unanswered instead of being guessed at.
/// </summary>
public static class ConsentReplyPolicy
{
    private static readonly string[] OptOutPhrases =
    [
        "หยุดส่ง", "เลิกส่ง", "ไม่ต้องส่ง", "อย่าส่ง", "อย่าติดต่อ", "อย่าเพิ่งติดต่อ", "บล็อก", "บล็อค", "ไม่ต้องติดต่อ",
        "opt out", "opt-out", "optout", "unsubscribe", "stop messaging", "do not contact", "dont contact", "don't contact"
    ];

    private static readonly string[] RefusalPhrases =
    [
        "ไม่ยินยอม", "ไม่อนุญาต", "ไม่ต้อง", "ไม่เอา", "ไม่สนใจ", "ปฏิเสธ", "ไม่ให้ส่ง",
        "no thanks", "not interested", "decline", "refuse", "dont ask", "don't ask"
    ];

    private static readonly string[] AcceptancePhrases =
    [
        "ตกลง", "ยินยอม", "อนุญาต", "ยอมรับ", "ได้ครับ", "ได้ค่ะ", "ได้เลย",
        "ok", "okay", "yes", "yeah", "yep", "sure", "confirm"
    ];

    private static readonly char[] Separators = [' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':'];

    public static ConsentReplyDecision Classify(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new(ConsentReplyVerdict.Unanswered, null);
        var normalized = Normalize(text);

        var optOut = MatchPhrase(normalized, OptOutPhrases);
        if (optOut is not null) return new(ConsentReplyVerdict.OptedOut, optOut);

        var refusal = MatchPhrase(normalized, RefusalPhrases);
        if (refusal is not null) return new(ConsentReplyVerdict.Rejected, refusal);

        var acceptance = MatchPhrase(normalized, AcceptancePhrases);
        if (acceptance is not null) return new(ConsentReplyVerdict.Granted, acceptance);

        return new(ConsentReplyVerdict.Unanswered, null);
    }

    private static string Normalize(string text) => text.Trim().ToLowerInvariant();

    /// <summary>
    /// Thai phrases are matched as substrings because Thai text is not space-separated; Latin words
    /// must appear on their own, otherwise "ok" inside "tokyo" would read as consent.
    /// </summary>
    private static string? MatchPhrase(string normalized, IReadOnlyList<string> phrases)
    {
        foreach (var phrase in phrases)
        {
            if (phrase.Length > 0 && phrase[0] < 0x80)
            {
                if (ContainsWord(normalized, phrase)) return phrase;
            }
            else if (normalized.Contains(phrase, StringComparison.Ordinal))
            {
                return phrase;
            }
        }
        return null;
    }

    private static bool ContainsWord(string normalized, string word)
    {
        var index = normalized.IndexOf(word, StringComparison.Ordinal);
        while (index >= 0)
        {
            if (IsBoundary(normalized, index, word.Length)) return true;
            index = normalized.IndexOf(word, index + 1, StringComparison.Ordinal);
        }
        return false;
    }

    private static bool IsBoundary(string text, int index, int length)
    {
        var beforeIsBoundary = index == 0 || IsWordBoundary(text[index - 1]);
        var after = index + length;
        var afterIsBoundary = after == text.Length || IsWordBoundary(text[after]);
        return beforeIsBoundary && afterIsBoundary;
    }

    private static bool IsWordBoundary(char value) => !char.IsLetterOrDigit(value) || Array.IndexOf(Separators, value) >= 0;
}
