using TelegramControlStudio.Core.Import;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class ContactCsvImporterTests
{
    [Fact]
    public void PreviewsValidAndInvalidRows()
    {
        var csv = "telegram_user_id,first_name,last_name,username,phone_number,has_consent,is_opted_out\n42,A,B,user,,true,false\nbad,C,D,,,true,false";
        var result = ContactCsvImporter.Preview(csv, 7);
        Assert.Single(result.Valid);
        Assert.Single(result.Errors);
        Assert.Equal(42, result.Valid[0].TelegramUserId);
    }
}

