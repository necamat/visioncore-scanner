using FluentAssertions;
using VisionCore.Domain.Imaging.Layout;
using VisionCore.Domain.Imaging.Recognition;
using Xunit;

namespace VisionCore.Tests.Unit.Domain.Imaging;

public sealed class RecognizedNumberTests
{
    [Theory]
    [InlineData(9)]
    [InlineData(-1)]
    [InlineData(10)]
    public void RecognizedDigit_Should_Reject_Non_Digit_Values(int value)
    {
        var act = () => new RecognizedDigit(FormRegion.Score, value, 0.9f);

        if (value is >= 0 and <= 9)
        {
            act.Should().NotThrow();
        }
        else
        {
            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }

    [Fact]
    public void Value_And_Text_Are_Built_From_Digits_Including_Leading_Zeros()
    {
        var number = new RecognizedNumber(new[]
        {
            new RecognizedDigit(FormRegion.ScoreDigit1, 0, 0.9f),
            new RecognizedDigit(FormRegion.ScoreDigit2, 7, 0.9f),
            new RecognizedDigit(FormRegion.ScoreDigit3, 5, 0.9f)
        });

        number.Value.Should().Be(75);
        number.Text.Should().Be("075");
    }

    [Fact]
    public void Confidence_Is_The_Average_Of_Digit_Confidences()
    {
        var number = new RecognizedNumber(new[]
        {
            new RecognizedDigit(FormRegion.TeamIdDigit1, 1, 0.8f),
            new RecognizedDigit(FormRegion.TeamIdDigit2, 2, 1.0f)
        });

        number.Confidence.Should().BeApproximately(0.9f, 1e-6f);
    }

    [Fact]
    public void Constructor_Should_Throw_For_Empty_Digits()
    {
        var act = () => new RecognizedNumber([]);

        act.Should().Throw<InvalidOperationException>();
    }
}
