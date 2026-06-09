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
        services.AddTransient<IScanSourceProvider, RoundFolderScanSourceProvider>();

        services.AddTransient<IRegionExtractor, PdfRegionExtractor>();
        services.AddTransient<ITeamIdRecognizer, TemplateMatchingTeamIdRecognizer>();
        services.AddTransient<IScoreRecognizer, TemplateMatchingScoreRecognizer>();

        // Construct explicitly via the (teamId, score) constructor — the type also
        // has a convenience IOptions constructor, which would make DI ambiguous.
        services.AddTransient<IDigitRecognizer>(sp => new TemplateMatchingDigitRecognizer(
            sp.GetRequiredService<ITeamIdRecognizer>(),
            sp.GetRequiredService<IScoreRecognizer>()));

        services.AddTransient<IPipelineFactory, PipelineFactory>();
        services.AddTransient<IExcelExporter, ClosedXmlExcelExporter>();

        return services;
    }
}
