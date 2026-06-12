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
    private static readonly DigitRecognitionOptions DigitOptions = new()
    {
        TemplateMatchThreshold = 0.55f,
        DarkPixelThreshold = 180
    };

    // Calibrated for the synthetic JPEG render: the accept bar sits above the
    // heuristic confidence cap (HeuristicConfidence.Strong) so heuristic reads
    // always route to review, and just below the weakest clean template read
    // (~0.79 — the "0" in the team-id box) so clean sheets auto-accept. The
    // evaluation gates on the weakest digit.
    private static readonly ConfidenceEvaluationOptions ConfidenceOptions = new()
    {
        MinimumAcceptedConfidence = 0.78f,
        MinimumReviewConfidence = 0.40f
    };

    public static PipelineFactory CreateFactory(PdfRegionOptions regions) =>
        new(
            new PdfRegionExtractor(Options.Create(regions)),
            new TemplateMatchingDigitRecognizer(Options.Create(DigitOptions)),
            Options.Create(ConfidenceOptions),
            NullLoggerFactory.Instance);

    public static ScanQuizSheetsUseCase CreateScanUseCase(PdfRegionOptions regions)
    {
        var sourceProvider = new RoundFolderScanSourceProvider(
            Options.Create(new ScanSourceOptions { RoundFolderPrefix = "R", SearchPatterns = ["*.pdf"] }));

        return new ScanQuizSheetsUseCase(
            sourceProvider,
            CreateFactory(regions),
            CreateStateRepository(regions),
            Options.Create(new ProcessingOptions()),
            NullLogger<ScanQuizSheetsUseCase>.Instance);
    }

    public static JsonProcessingStateRepository CreateStateRepository(PdfRegionOptions regions) =>
        new(
            Options.Create(DigitOptions),
            Options.Create(ConfidenceOptions),
            Options.Create(regions),
            NullLogger<JsonProcessingStateRepository>.Instance);
}
