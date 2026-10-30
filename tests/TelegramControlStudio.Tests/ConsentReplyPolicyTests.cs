using TelegramControlStudio.Core.Safety;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class ConsentReplyPolicyTests
{
    [Theory]
    [InlineData("ตกลง")]
    [InlineData("ยินยอมครับ")]
    [InlineData("อนุญาตให้ส่งได้")]
    [InlineData("ได้เลย")]
    [InlineData("OK")]
    [InlineData("ok 👍")]
    [InlineData("yes, sure")]
    public void RecognisesAffirmativeAnswers(string text)
    {
        var decision = ConsentReplyPolicy.Classify(text);
        Assert.Equal(ConsentReplyVerdict.Granted, decision.Verdict);
        Assert.False(string.IsNullOrWhiteSpace(decision.MatchedKeyword));
    }

    [Theory]
    [InlineData("หยุดส่งข้อความ")]
    [InlineData("ไม่ต้องส่งมาอีก")]
    [InlineData("อย่าเพิ่งติดต่อ")]
    [InlineData("บล็อก")]
    [InlineData("Unsubscribe")]
    [InlineData("please opt out")]
    [InlineData("STOP MESSAGING")]
    public void RecognisesOptOuts(string text) =>
        Assert.Equal(ConsentReplyVerdict.OptedOut, ConsentReplyPolicy.Classify(text).Verdict);

    [Theory]
    [InlineData("ไม่ยินยอม")]
    [InlineData("ไม่ต้อง")]
    [InlineData("ไม่สนใจ")]
    [InlineData("no thanks")]
    [InlineData("I am not interested")]
    public void RecognisesRefusals(string text) =>
        Assert.Equal(ConsentReplyVerdict.Rejected, ConsentReplyPolicy.Classify(text).Verdict);

    [Fact]
    public void NegatedConsentIsNeverReadAsConsent()
    {
        // "ไม่ยินยอม" contains "ยินยอม", so refusal must be checked before acceptance.
        Assert.Equal(ConsentReplyVerdict.Rejected, ConsentReplyPolicy.Classify("ไม่ยินยอม").Verdict);
        Assert.Equal(ConsentReplyVerdict.Granted, ConsentReplyPolicy.Classify("ยินยอม").Verdict);
    }

    [Fact]
    public void OptOutOutranksAnAffirmationInTheSameMessage() =>
        Assert.Equal(ConsentReplyVerdict.OptedOut, ConsentReplyPolicy.Classify("ตกลง แต่หยุดส่งที").Verdict);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("สวัสดีครับ")]
    [InlineData("ทำไมต้องถาม")]
    [InlineData("tokyo")]
    [InlineData("I need notice about the meeting")]
    public void UnclearTextChangesNothing(string? text)
    {
        var decision = ConsentReplyPolicy.Classify(text);
        Assert.Equal(ConsentReplyVerdict.Unanswered, decision.Verdict);
        Assert.Null(decision.MatchedKeyword);
        Assert.False(decision.ChangesConsent);
    }

    [Fact]
    public void OnlyGrantAndOptOutTouchTheConsentFlags()
    {
        Assert.True(ConsentReplyPolicy.Classify("ok").ChangesConsent);
        Assert.True(ConsentReplyPolicy.Classify("opt out").ChangesConsent);
        Assert.False(ConsentReplyPolicy.Classify("no thanks").ChangesConsent);
    }
}
