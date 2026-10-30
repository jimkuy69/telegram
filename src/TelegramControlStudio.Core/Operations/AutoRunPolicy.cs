using System.Collections.Concurrent;
using TelegramControlStudio.Core.Domain;

namespace TelegramControlStudio.Core.Operations;

/// <summary>
/// Decides which jobs the desktop auto runner may pick up without another human click.
/// A human-paused job stays paused: pausing is an instruction, not a failure.
/// </summary>
public static class AutoRunPolicy
{
    public static bool IsEligible(Job job, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (string.IsNullOrWhiteSpace(job.MessageText)) return false;
        return job.Status == JobStatus.Queued ||
            (job.Status == JobStatus.WaitingForFloodWait && job.FloodWaitUntil is { } until && until <= now);
    }
}

/// <summary>
/// Process-wide guard so the auto runner and a manual "run" click never drive the same job
/// from two threads, which would double-send to every recipient.
/// </summary>
public sealed class JobInFlightGate
{
    private readonly ConcurrentDictionary<Guid, byte> _inFlight = new();

    public int Count => _inFlight.Count;

    public bool IsRunning(Guid jobId) => _inFlight.ContainsKey(jobId);

    public bool TryBegin(Guid jobId) => _inFlight.TryAdd(jobId, 0);

    public void End(Guid jobId) => _inFlight.TryRemove(jobId, out _);
}
