namespace TelegramControlStudio.Core.Domain;

public sealed record DashboardMetrics(
    int TotalJobs,
    int CompletedJobs,
    int FailedJobs,
    int QueuedJobs,
    int FloodWaitJobs,
    int PermissionErrors,
    int PrivacyErrors,
    int NetworkErrors,
    int CampaignJobs,
    int CampaignCompletedJobs,
    int CampaignFailedJobs);

