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
            // Calibrated for the synthetic JPEG render: the accept bar sits
            // above the heuristic confidence cap (0.70) so heuristic reads
            // always route to review, and just below the weakest clean
            // template read (~0.79 — the "0" in the team-id box) so clean
            // sheets auto-accept. The evaluation gates on the weakest digit.
            Options.Create(new ConfidenceEvaluationOptions
            {
                MinimumAcceptedConfidence = 0.78f,
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
            new JsonProcessingStateRepository(NullLogger<JsonProcessingStateRepository>.Instance),
            Options.Create(new ProcessingOptions()),
            NullLogger<ScanQuizSheetsUseCase>.Instance);
    }
}
