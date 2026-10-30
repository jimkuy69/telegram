using TelegramControlStudio.Core.Safety;

namespace TelegramControlStudio.Core.Operations;

/// <summary>
/// <c>jobs.target_type</c> is free-form text, but the runner needs to know which safety gate a job
/// was built under. Centralising the parsing keeps that decision out of string comparisons at every call site.
/// A consent-request job optionally carries the template that produced its text: <c>ConsentRequest</c> or
/// <c>ConsentRequest:123</c>.
/// </summary>
public static class JobTargetTypes
{
    public const string ConsentRequest = "ConsentRequest";

    private const string Separator = ":";

    public static string ConsentRequestTargetType(long? templateId) =>
        templateId is { } id && id > 0 ? ConsentRequest + Separator + id : ConsentRequest;

    public static bool IsConsentRequest(string? targetType) =>
        targetType is not null &&
        (string.Equals(targetType, ConsentRequest, StringComparison.Ordinal) ||
         targetType.StartsWith(ConsentRequest + Separator, StringComparison.Ordinal));

    public static long? TemplateIdOf(string? targetType)
    {
        if (!IsConsentRequest(targetType)) return null;
        var separator = targetType!.IndexOf(Separator, StringComparison.Ordinal);
        if (separator < 0) return null;
        var parsed = long.TryParse(targetType.AsSpan(separator + 1), out var templateId) && templateId > 0 ? templateId : (long?)null;
        return parsed;
    }

    public static SendPurpose PurposeOf(string? targetType) =>
        IsConsentRequest(targetType) ? SendPurpose.ConsentRequest : SendPurpose.Campaign;
}
