using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Core.Operations;
using TelegramControlStudio.Core.Safety;

namespace TelegramControlStudio.Infrastructure.Persistence;

public sealed record ConsentRequestCandidate(long TelegramUserId, string DisplayName, bool Eligible, string ReasonCode);

/// <summary>
/// What an opt-in request would do, computed before anything is queued. <see cref="RemainingToday"/>
/// is the account's daily cap left after the requests already sent, and it is also the ceiling on
/// <see cref="Eligible"/> so the number the operator confirms is the number that can really go out.
/// </summary>
public sealed record ConsentRequestPlan(
    IReadOnlyList<ConsentRequestCandidate> Eligible,
    IReadOnlyList<ConsentRequestCandidate> Excluded,
    int DailyLimit,
    int RequestedToday,
    int RemainingToday,
    int CooldownDays,
    bool KillSwitchEngaged,
    bool TestModeEnabled);

public sealed record ConsentRequestQueueResult(Guid? JobId, ConsentRequestPlan Plan)
{
    public bool Created => JobId is not null;
}

/// <summary>
/// Turns a selection in the Contacts tab into a job that waits for confirmation in the Queue window.
/// The plan is evaluated with the same gate the runner applies at send time, so queueing cannot
/// promise more opt-in requests than the cooldown and daily quota actually allow.
/// </summary>
public sealed class ConsentRequestQueueService(string databasePath)
{
    public const string UnknownContactReason = "CONTACT_NOT_FOUND";

    public async Task<ConsentRequestPlan> PlanAsync(
        long accountId,
        IReadOnlyCollection<long> telegramUserIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(telegramUserIds);
        var contacts = (await new ContactRepository(databasePath).ListAsync(accountId, cancellationToken)).ToDictionary(c => c.TelegramUserId);
        var limits = await new ConsentRequestSettingsRepository(databasePath).GetAsync(cancellationToken);
        var requests = new ConsentRequestRepository(databasePath);
        var safety = await new SendSafetyRepository(databasePath).GetAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var requestedToday = await requests.CountRequestedSinceAsync(accountId, now.AddDays(-1), cancellationToken);
        var latestEvents = await requests.LatestByContactAsync(accountId, cancellationToken);
        var remaining = Math.Max(0, limits.DailyLimit - requestedToday);

        var eligible = new List<ConsentRequestCandidate>();
        var excluded = new List<ConsentRequestCandidate>();
        foreach (var userId in telegramUserIds.Where(id => id > 0).Distinct().Order())
        {
            if (!contacts.TryGetValue(userId, out var contact))
            {
                excluded.Add(new ConsentRequestCandidate(userId, userId.ToString(), false, UnknownContactReason));
                continue;
            }

            var eligibility = new ConsentRequestEligibility(
                userId,
                !string.IsNullOrWhiteSpace(contact.PhoneNumber),
                contact.HasConsent,
                contact.IsOptedOut,
                await requests.LastRequestedAtAsync(accountId, userId, cancellationToken),
                requestedToday,
                limits.CooldownDays,
                limits.DailyLimit,
                now,
                latestEvents.TryGetValue(userId, out var latest) && latest.Kind == ConsentRequestKinds.Rejected);
            var decision = SendSafetyPolicy.EvaluateConsentRequest(safety, eligibility);
            if (decision.CanSend && eligible.Count >= remaining)
                decision = new SendSafetyDecision(SendSafetyVerdict.Skip, ConsentRequestPolicy.DailyLimitReason);
            var candidate = new ConsentRequestCandidate(userId, DisplayName(contact), decision.CanSend, decision.ReasonCode);
            (decision.CanSend ? eligible : excluded).Add(candidate);
        }

        return new ConsentRequestPlan(
            eligible, excluded, limits.DailyLimit, requestedToday, remaining, limits.CooldownDays,
            safety.KillSwitchEngaged, safety.TestModeEnabled);
    }

    public async Task<ConsentRequestQueueResult> QueueAsync(
        long accountId,
        IReadOnlyCollection<long> telegramUserIds,
        string messageText,
        long? templateId,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        if (!confirmed) throw new InvalidOperationException("CONSENT_REQUEST_CONFIRMATION_REQUIRED");
        ArgumentException.ThrowIfNullOrWhiteSpace(messageText);

        var plan = await PlanAsync(accountId, telegramUserIds, cancellationToken);
        if (plan.Eligible.Count == 0) return new ConsentRequestQueueResult(null, plan);

        var recipientIds = plan.Eligible.Select(candidate => candidate.TelegramUserId).ToArray();
        // Re-confirming the same group on the same day returns the job that is already waiting for
        // confirmation instead of queueing a second wave of identical requests.
        var definition = $"consent-request:{accountId}|{templateId?.ToString(CultureInfo.InvariantCulture) ?? "none"}|{DateTimeOffset.UtcNow:yyyy-MM-dd}|{string.Join(",", recipientIds)}";
        var jobs = new JobRepository(databasePath);
        var jobId = await jobs.CreateAsync(
            accountId,
            JobTargetTypes.ConsentRequestTargetType(templateId),
            recipientIds.Length,
            Hash(definition),
            messageText.Trim(),
            null,
            cancellationToken);

        var items = new JobItemRepository(databasePath);
        if ((await items.ListAsync(jobId, cancellationToken)).Count == 0)
        {
            await items.CreateAsync(recipientIds.Select(userId => new JobItem(
                jobId,
                accountId,
                userId,
                JobStatus.WaitingForConfirmation.ToString(),
                0,
                Hash($"consent-request-item:{jobId:D}:{userId}"),
                null,
                DateTimeOffset.UtcNow)), cancellationToken);
        }

        await new ActivityLogRepository(databasePath).AppendAsync(new ActivityLog(
            accountId,
            jobId,
            "Info",
            "ConsentRequestJobCreated",
            $"Queued {recipientIds.Length} opt-in request(s); {plan.Excluded.Count} recipient(s) excluded by the request policy.",
            DateTimeOffset.UtcNow), cancellationToken);

        return new ConsentRequestQueueResult(jobId, plan);
    }

    private static string DisplayName(Contact contact)
    {
        var name = $"{contact.FirstName} {contact.LastName}".Trim();
        return string.IsNullOrWhiteSpace(name) ? contact.Username ?? contact.TelegramUserId.ToString() : name;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
