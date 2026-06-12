using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VisionCore.Application.Configuration;
using Xunit;

namespace VisionCore.Tests.Unit.Application.Configuration;

/// <summary>
/// Verifies that the DataAnnotations + ValidateOnStart wiring rejects
/// out-of-range option values, matching how AppHost validates configuration.
/// </summary>
public sealed class OptionsValidationTests
{
    [Fact]
    public void DigitRecognitionOptions_Should_Be_Valid_With_Defaults()
    {
        var options = Resolve<DigitRecognitionOptions>(new Dictionary<string, string?>());

        var act = () => _ = options.Value;

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("DarkPixelThreshold", "999")]
    [InlineData("TemplateMatchThreshold", "-0.1")]
    [InlineData("MinimumInkRatio", "5")]
    public void DigitRecognitionOptions_Should_Fail_When_Value_Out_Of_Range(string key, string value)
    {
        var options = Resolve<DigitRecognitionOptions>(new Dictionary<string, string?> { [key] = value });

        var act = () => _ = options.Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void ConfidenceEvaluationOptions_Should_Fail_When_Confidence_Out_Of_Range()
    {
        var options = Resolve<ConfidenceEvaluationOptions>(
            new Dictionary<string, string?> { ["MinimumAcceptedConfidence"] = "2" });

        var act = () => _ = options.Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void PdfRegionOptions_Should_Pass_For_Plausible_Regions()
    {
        var validator = new PdfRegionOptionsValidator();

        var result = validator.Validate(null, PlausibleRegions());

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void PdfRegionOptions_Should_Fail_When_The_Section_Did_Not_Bind()
    {
        // An unbound or mistyped "PdfRegions" section leaves every rectangle
        // at its 1x1 default — that must fail at startup, not crop air later.
        var validator = new PdfRegionOptionsValidator();

        var result = validator.Validate(null, new PdfRegionOptions());

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("PdfRegions");
    }

    [Theory]
    [InlineData(-1, 0, 100, 100)]
    [InlineData(0, -5, 100, 100)]
    [InlineData(0, 0, 2, 100)]
    [InlineData(0, 0, 100, 2)]
    public void PdfRegionOptions_Should_Fail_For_Degenerate_Bounds(int x, int y, int width, int height)
    {
        var validator = new PdfRegionOptionsValidator();
        var options = PlausibleRegions() with
        {
            Score = new PdfRegionBounds { X = x, Y = y, Width = width, Height = height }
        };

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Score");
    }

    [Fact]
    public void ConfidenceEvaluationOptions_Should_Pass_For_The_Defaults()
    {
        var validator = new ConfidenceEvaluationOptionsValidator();

        var result = validator.Validate(null, new ConfidenceEvaluationOptions());

        result.Succeeded.Should().BeTrue(result.FailureMessage);
    }

    [Fact]
    public void ConfidenceEvaluationOptions_Should_Fail_When_Review_Floor_Reaches_The_Accept_Bar()
    {
        var validator = new ConfidenceEvaluationOptionsValidator();
        var options = new ConfidenceEvaluationOptions
        {
            MinimumAcceptedConfidence = 0.80f,
            MinimumReviewConfidence = 0.80f
        };

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("below");
    }

    [Fact]
    public void ConfidenceEvaluationOptions_Should_Fail_When_The_Accept_Bar_Would_Auto_Accept_Heuristics()
    {
        // The whole point of the review mechanism: a heuristic guess must
        // never clear the accept bar, no matter how the config is edited.
        var validator = new ConfidenceEvaluationOptionsValidator();
        var options = new ConfidenceEvaluationOptions
        {
            MinimumAcceptedConfidence = HeuristicConfidence.Strong,
            MinimumReviewConfidence = 0.40f
        };

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("heuristic");
    }

    [Fact]
    public void ConfidenceEvaluationOptions_Should_Fail_When_The_Review_Floor_Would_Reject_Heuristics()
    {
        var validator = new ConfidenceEvaluationOptionsValidator();
        var options = new ConfidenceEvaluationOptions
        {
            MinimumAcceptedConfidence = 0.85f,
            MinimumReviewConfidence = HeuristicConfidence.Weak + 0.01f
        };

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("rejected instead of reviewed");
    }

    [Fact]
    public void PdfRegionOptions_Should_Fail_For_An_Implausible_Dpi()
    {
        var validator = new PdfRegionOptionsValidator();
        var options = PlausibleRegions() with { Dpi = 10 };

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Dpi");
    }

    private static PdfRegionOptions PlausibleRegions()
    {
        var bounds = new PdfRegionBounds { X = 100, Y = 100, Width = 80, Height = 90 };
        return new PdfRegionOptions
        {
            Dpi = 200,
            TeamId = bounds,
            TeamIdDigit1 = bounds,
            TeamIdDigit2 = bounds,
            Score = bounds,
            ScoreDigit1 = bounds,
            ScoreDigit2 = bounds,
            ScoreDigit3 = bounds
        };
    }

    private static IOptions<TOptions> Resolve<TOptions>(IDictionary<string, string?> values)
        where TOptions : class
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection();
        services.AddOptions<TOptions>()
            .Bind(configuration)
            .ValidateDataAnnotations();

        return services.BuildServiceProvider().GetRequiredService<IOptions<TOptions>>();
    }
}
