using System.Security.Cryptography;
using System.Text;
using TelegramControlStudio.Core.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TelegramLoginVerifierTests
{
    private const string BotToken = "123456789:test-token-value-for-verifier";
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AcceptsCorrectlySignedFreshPayload()
    {
        var fields = Sign(new Dictionary<string, string>
        {
            ["id"] = "555001",
            ["first_name"] = "Somchai",
            ["last_name"] = "Test",
            ["username"] = "somchai",
            ["auth_date"] = Now.ToUnixTimeSeconds().ToString()
        });

        Assert.True(TelegramLoginVerifier.Verify(fields, BotToken, Now));
    }

    [Fact]
    public void RejectsTamperedField()
    {
        var fields = Sign(new Dictionary<string, string>
        {
            ["id"] = "555001",
            ["first_name"] = "Somchai",
            ["auth_date"] = Now.ToUnixTimeSeconds().ToString()
        });
        fields["id"] = "555002";

        Assert.False(TelegramLoginVerifier.Verify(fields, BotToken, Now));
    }

    [Fact]
    public void RejectsWrongBotToken()
    {
        var fields = Sign(new Dictionary<string, string>
        {
            ["id"] = "555001",
            ["auth_date"] = Now.ToUnixTimeSeconds().ToString()
        });

        Assert.False(TelegramLoginVerifier.Verify(fields, "987654321:other-token", Now));
    }

    [Fact]
    public void RejectsStaleAuthDate()
    {
        var fields = Sign(new Dictionary<string, string>
        {
            ["id"] = "555001",
            ["auth_date"] = Now.AddHours(-25).ToUnixTimeSeconds().ToString()
        });

        Assert.False(TelegramLoginVerifier.Verify(fields, BotToken, Now));
    }

    [Fact]
    public void RejectsFutureAuthDate()
    {
        var fields = Sign(new Dictionary<string, string>
        {
            ["id"] = "555001",
            ["auth_date"] = Now.AddMinutes(10).ToUnixTimeSeconds().ToString()
        });

        Assert.False(TelegramLoginVerifier.Verify(fields, BotToken, Now));
    }

    [Fact]
    public void RejectsMissingOrEmptyHash()
    {
        var fields = Sign(new Dictionary<string, string>
        {
            ["id"] = "555001",
            ["auth_date"] = Now.ToUnixTimeSeconds().ToString()
        });
        fields.Remove("hash");
        Assert.False(TelegramLoginVerifier.Verify(fields, BotToken, Now));

        fields["hash"] = " ";
        Assert.False(TelegramLoginVerifier.Verify(fields, BotToken, Now));
    }

    [Fact]
    public void RejectsMissingAuthDate()
    {
        var fields = Sign(new Dictionary<string, string> { ["id"] = "555001" });
        Assert.False(TelegramLoginVerifier.Verify(fields, BotToken, Now));
    }

    [Fact]
    public void IgnoresEmptyOptionalFieldsWhenSigning()
    {
        // Telegram omits empty optional values from the data-check-string.
        var fields = Sign(new Dictionary<string, string>
        {
            ["id"] = "555001",
            ["first_name"] = "Somchai",
            ["username"] = "",
            ["auth_date"] = Now.ToUnixTimeSeconds().ToString()
        });

        Assert.True(TelegramLoginVerifier.Verify(fields, BotToken, Now));
    }

    private static Dictionary<string, string> Sign(Dictionary<string, string> fields)
    {
        var checkString = string.Join("\n", fields
            .Where(pair => pair.Key != "hash" && !string.IsNullOrEmpty(pair.Value))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}"));
        var secret = SHA256.HashData(Encoding.UTF8.GetBytes(BotToken));
        var hash = Convert.ToHexString(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(checkString))).ToLowerInvariant();
        var signed = new Dictionary<string, string>(fields, StringComparer.Ordinal) { ["hash"] = hash };
        return signed;
    }
}
