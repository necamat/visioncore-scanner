using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VisionCore.Application.Abstractions;
using VisionCore.Infrastructure;
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

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddScanningInfrastructure();
        return services.BuildServiceProvider();
    }
}
