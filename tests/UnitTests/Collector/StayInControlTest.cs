using JetBrains.Annotations;
using Microsoft.Extensions.Time.Testing;
using Onyx.Collector.Core.Control;
using Onyx.Collector.Core.Crypto;
using Shouldly;
using Xunit;

namespace UnitTests.Collector;

[TestSubject(typeof(StayInControl))]
public class StayInControlTest
{
    private readonly FakeTimeProvider m_Time = new(new DateTimeOffset(2026, 11, 28, 9, 0, 0, TimeSpan.Zero));

    private static ControlSettings Enabled() => new()
    {
        Enabled = true,
        GraceMinutes = 10,
        Exempt = new AppSelection { Apps = ["android:com.maps"], Categories = ["Productivity"] }
    };

    [Fact]
    public void AsksUntilAnsweredCorrectly_ThenNotDuringTheGracePeriod()
    {
        var control = new StayInControl(m_Time);
        var settings = Enabled();

        control.ShouldAsk(settings, 1, "android:com.chat", "Social").ShouldBeTrue();
        control.Grant(settings, "android:com.chat");
        control.ShouldAsk(settings, 1, "android:com.chat", "Social").ShouldBeFalse();
        control.ShouldAsk(settings, 1, "android:com.video", "Entertainment").ShouldBeTrue();

        m_Time.Advance(TimeSpan.FromMinutes(11));
        control.ShouldAsk(settings, 1, "android:com.chat", "Social").ShouldBeTrue();
    }

    [Fact]
    public void ExemptAppsCategoriesOtherDevicesAndOffNeverAsk()
    {
        var control = new StayInControl(m_Time);
        var settings = Enabled();

        control.ShouldAsk(settings, 1, "android:com.maps", "Travel").ShouldBeFalse();
        control.ShouldAsk(settings, 1, "android:com.notes", "Productivity").ShouldBeFalse();

        settings.Devices = [2];
        control.ShouldAsk(settings, 1, "android:com.chat", null).ShouldBeFalse();
        control.ShouldAsk(settings, 2, "android:com.chat", null).ShouldBeTrue();

        settings.Enabled = false;
        control.ShouldAsk(settings, 2, "android:com.chat", null).ShouldBeFalse();
    }

    [Theory]
    [InlineData("Vienna|Wien", "  wien ", true)]
    [InlineData("Brasília|Brasilia", "BRASÍLIA", true)]
    [InlineData("Washington|Washington, D.C.", "washington d.c.", true)]
    [InlineData("Bogotá", "bogota", true)]
    [InlineData("42", "042", true)]
    [InlineData("42", "43", false)]
    [InlineData("colour|color", "colours", false)]
    [InlineData("Berlin", "", false)]
    public void IsCorrect_IgnoresCaseAccentsAndPunctuation(string answer, string given, bool correct) =>
        StayInControl.IsCorrect(new ControlQuestion("?", answer), given).ShouldBe(correct);

    [Fact]
    public void GeneratedQuestions_AreAnsweredByTheirOwnAnswer()
    {
        var random = new Random(7);
        foreach (var subject in new[] { ControlSubject.Arithmetic, ControlSubject.Capitals })
        {
            for (var i = 0; i < 50; i++)
            {
                var question = StayInControl.NextQuestion(new ControlSettings { Subject = subject }, random);
                StayInControl.IsCorrect(question, question.Answer.Split('|')[0]).ShouldBeTrue(question.Question);
            }
        }
    }

    [Fact]
    public void OwnQuestions_FallBackToArithmeticWhileThereAreNone()
    {
        var random = new Random(1);
        StayInControl.NextQuestion(new ControlSettings { Subject = ControlSubject.Own }, random)
            .Question.ShouldEndWith("= ?");

        var own = new ControlSettings { Subject = ControlSubject.Own, Questions = [new("der Hund", "the dog")] };
        StayInControl.NextQuestion(own, random).ShouldBe(new ControlQuestion("der Hund", "the dog"));
    }
}
