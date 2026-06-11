using FluentAssertions;
using VisionCore.Application.Configuration;
using VisionCore.Infrastructure.Implementations.Recognition;
using Xunit;

namespace VisionCore.Tests.Unit.Infrastructure;

public sealed class TemplateMatchingNumberRecognizerTests
{
    [Fact]
    public void ScaleByMargin_Should_Report_The_Raw_Agreement_When_The_Margin_Is_Wide()
    {
        // Margin 0.05 is past the full-certainty window (0.02): unchanged.
        Probe.Scale(0.90f, 0.85f).Should().BeApproximately(0.90f, 0.0001f);
    }

    [Fact]
    public void ScaleByMargin_Should_Apply_The_Certainty_Floor_To_A_Dead_Tie()
    {
        // A dead tie keeps only the minimum certainty (0.85) of its agreement.
        Probe.Scale(0.90f, 0.90f).Should().BeApproximately(0.90f * 0.85f, 0.0001f);
    }

    [Fact]
    public void ScaleByMargin_Should_Interpolate_Linearly_Inside_The_Window()
    {
        // Margin 0.01 = half the window: certainty 0.85 + 0.15 * 0.5 = 0.925.
        Probe.Scale(0.90f, 0.89f).Should().BeApproximately(0.90f * 0.925f, 0.0001f);
    }

    [Fact]
    public void ScaleByMargin_Should_Order_Candidates_By_Margin_At_Equal_Agreement()
    {
        var ambiguous = Probe.Scale(0.88f, 0.87f);
        var clear = Probe.Scale(0.88f, 0.80f);

        ambiguous.Should().BeLessThan(clear, "the clearer winner must report higher confidence");
    }

    /// <summary>Exposes the protected margin scaling for direct verification.</summary>
    private sealed class Probe(DigitRecognitionOptions options) : TemplateMatchingNumberRecognizer(options)
    {
        public static float Scale(float bestAgreement, float bestOtherDigitAgreement) =>
            ScaleByMargin(bestAgreement, bestOtherDigitAgreement);
    }
}
