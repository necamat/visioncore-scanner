using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VisionCore.Application.Configuration;
using VisionCore.Application.UseCases;
using VisionCore.Infrastructure.Factories;
using VisionCore.Infrastructure.Implementations;
using VisionCore.Infrastructure.Implementations.Pdf;
using VisionCore.Infrastructure.Implementations.Recognition;

namespace VisionCore.Tests.Integration.EndToEnd;

/// <summary>
/// Builds the production PDF pieces — factory, scan-source provider and scan use
/// case — wired with thresholds tuned for the synthetic / sample sheets. Shared
/// by the PDF end-to-end tests so they exercise the real components.
/// </summary>
internal static class PdfTestPipeline
{
    public static PipelineFactory CreateFactory(PdfRegionOptions regions) =>
        new(
            new PdfRegionExtractor(Options.Create(regions)),
            new TemplateMatchingDigitRecognizer(Options.Create(new DigitRecognitionOptions
            {
                TemplateMatchThreshold = 0.55f,
                DarkPixelThreshold = 180
            })),
            Options.Create(new ConfidenceEvaluationOptions
            {
                MinimumAcceptedConfidence = 0.55f,
                MinimumReviewConfidence = 0.40f
            }),
            NullLoggerFactory.Instance);

    public static ScanQuizSheetsUseCase CreateScanUseCase(PdfRegionOptions regions)
    {
        var sourceProvider = new RoundFolderScanSourceProvider(
            Options.Create(new ScanSourceOptions { RoundFolderPrefix = "R", SearchPatterns = ["*.pdf"] }));

        return new ScanQuizSheetsUseCase(
            sourceProvider,
            CreateFactory(regions),
            Options.Create(new ProcessingOptions()),
            NullLogger<ScanQuizSheetsUseCase>.Instance);
    }
}
