namespace TelegramControlStudio.Core.Telegram;

public static class TdLibParametersBuilder
{
    public static IReadOnlyDictionary<string, object?> Build(
        int apiId,
        string apiHash,
        string databaseDirectory,
        string filesDirectory,
        string systemLanguageCode = "en",
        string applicationVersion = "0.1.0",
        string deviceModel = "Telegram Control Studio",
        string systemVersion = "Windows")
    {
        if (apiId <= 0) throw new ArgumentOutOfRangeException(nameof(apiId));
        ArgumentException.ThrowIfNullOrWhiteSpace(apiHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(filesDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemLanguageCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceModel);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemVersion);
        return new Dictionary<string, object?>
        {
            ["api_id"] = apiId,
            ["api_hash"] = apiHash,
            ["database_directory"] = databaseDirectory,
            ["files_directory"] = filesDirectory,
            ["database_encryption_key"] = string.Empty,
            ["use_file_database"] = true,
            ["use_chat_info_database"] = true,
            ["use_message_database"] = true,
            ["use_secret_chats"] = false,
            ["system_language_code"] = systemLanguageCode,
            ["device_model"] = deviceModel,
            ["system_version"] = systemVersion,
            ["application_version"] = applicationVersion,
            ["enable_storage_optimizer"] = true
        };
    }
}
