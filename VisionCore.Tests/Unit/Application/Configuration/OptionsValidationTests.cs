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
