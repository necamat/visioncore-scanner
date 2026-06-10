using Microsoft.Extensions.DependencyInjection;
using VisionCore.Application.Abstractions;
using VisionCore.Infrastructure.Factories;
using VisionCore.Infrastructure.Implementations;
using VisionCore.Infrastructure.Implementations.Recognition;
using VisionCore.Infrastructure.Implementations.Pdf;

namespace VisionCore.Infrastructure;

/// <summary>
/// Registers the scanning infrastructure: the scan-source provider, region
/// extraction, digit recognition, the pipeline factory and the Excel exporter.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddScanningInfrastructure(this IServiceCollection services)
    {
        // Every service here is stateless and thread-safe after construction,
        // so singletons are the natural lifetime. For the recognizers it also
        // matters for cost: they render their full digit-template sets in the
        // constructor, which must happen once per process, not per resolution.
        services.AddSingleton<IScanSourceProvider, RoundFolderScanSourceProvider>();

        services.AddSingleton<IRegionExtractor, PdfRegionExtractor>();
        services.AddSingleton<ITeamIdRecognizer, TemplateMatchingTeamIdRecognizer>();
        services.AddSingleton<IScoreRecognizer, TemplateMatchingScoreRecognizer>();

        // Construct explicitly via the (teamId, score) constructor — the type also
        // has a convenience IOptions constructor, which would make DI ambiguous.
        services.AddSingleton<IDigitRecognizer>(sp => new TemplateMatchingDigitRecognizer(
            sp.GetRequiredService<ITeamIdRecognizer>(),
            sp.GetRequiredService<IScoreRecognizer>()));

        services.AddSingleton<IPipelineFactory, PipelineFactory>();
        services.AddSingleton<IExcelExporter, ClosedXmlExcelExporter>();

        return services;
    }
}
