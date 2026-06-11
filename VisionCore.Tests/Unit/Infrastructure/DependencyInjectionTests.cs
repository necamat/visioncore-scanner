using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VisionCore.Application.Abstractions;
using VisionCore.Application.Configuration;
using VisionCore.Infrastructure;
using VisionCore.Infrastructure.Implementations.Recognition;
using Xunit;

namespace VisionCore.Tests.Unit.Infrastructure;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddScanningInfrastructure_Should_Register_Every_Port_With_An_Implementation()
    {
        using var provider = BuildProvider();

        provider.GetRequiredService<IScanSourceProvider>().Should().NotBeNull();
        provider.GetRequiredService<IRegionExtractor>().Should().NotBeNull();
        provider.GetRequiredService<ITeamIdRecognizer>().Should().NotBeNull();
        provider.GetRequiredService<IScoreRecognizer>().Should().NotBeNull();
        provider.GetRequiredService<IDigitRecognizer>().Should().NotBeNull();
        provider.GetRequiredService<IPipelineFactory>().Should().NotBeNull();
        provider.GetRequiredService<IExcelExporter>().Should().NotBeNull();
        provider.GetRequiredService<IReviewedScansReader>().Should().NotBeNull();
        provider.GetRequiredService<IProcessingStateRepository>().Should().NotBeNull();
    }

    [Fact]
    public void AddScanningInfrastructure_Should_Register_Stateless_Services_As_Singletons()
    {
        using var provider = BuildProvider();

        provider.GetRequiredService<IDigitRecognizer>().Should().BeSameAs(
            provider.GetRequiredService<IDigitRecognizer>(),
            "digit templates are rendered in the constructor and must be built once per process");
        provider.GetRequiredService<ITeamIdRecognizer>().Should().BeSameAs(
            provider.GetRequiredService<ITeamIdRecognizer>());
        provider.GetRequiredService<IScoreRecognizer>().Should().BeSameAs(
            provider.GetRequiredService<IScoreRecognizer>());
        provider.GetRequiredService<IPipelineFactory>().Should().BeSameAs(
            provider.GetRequiredService<IPipelineFactory>());
        provider.GetRequiredService<IExcelExporter>().Should().BeSameAs(
            provider.GetRequiredService<IExcelExporter>());
    }

    [Fact]
    public void AddScanningInfrastructure_Should_Use_Template_Matching_For_The_Score_By_Default()
    {
        using var provider = BuildProvider();

        provider.GetRequiredService<IScoreRecognizer>()
            .Should().BeOfType<TemplateMatchingScoreRecognizer>();
    }

    [Fact]
    public void AddScanningInfrastructure_Should_Switch_The_Score_Engine_To_Onnx_From_Options()
    {
        using var provider = BuildProvider(new DigitRecognitionOptions
        {
            ScoreEngine = ScoreRecognitionEngine.Onnx,
            OnnxModelPath = "Models/mnist-12.onnx"
        });

        provider.GetRequiredService<IScoreRecognizer>().Should().BeOfType<OnnxScoreRecognizer>();
    }

    private static ServiceProvider BuildProvider(DigitRecognitionOptions? digitOptions = null)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddScanningInfrastructure();

        if (digitOptions is not null)
        {
            services.AddSingleton(Options.Create(digitOptions));
        }

        return services.BuildServiceProvider();
    }
}
