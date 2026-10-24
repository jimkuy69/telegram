namespace TelegramControlStudio.Core.Domain;

public enum JobStatus
{
    Draft,
    WaitingForConfirmation,
    Queued,
    Running,
    Paused,
    WaitingForFloodWait,
    Completed,
    PartiallyCompleted,
    Failed,
    Cancelled
}
