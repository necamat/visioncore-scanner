namespace VisionCore.Infrastructure.Factories;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VisionCore.Application.Abstractions;
using VisionCore.Application.Configuration;
using VisionCore.Application.Imaging;
using VisionCore.Application.Imaging.Steps.Pdf;
using VisionCore.Application.Imaging.Steps.Shared;

/// <inheritdoc />
public sealed class PipelineFactory(
    IRegionExtractor pdfRegionExtractor,
    IDigitRecognizer digitRecognizer,
    IOptions<ConfidenceEvaluationOptions> confidenceOptions,
    ILoggerFactory loggerFactory) : IPipelineFactory
{
    /// <inheritdoc />
    public IImageProcessingPipeline CreateForSource(string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();

        return extension switch
        {
            ".pdf" => CreatePdfPipeline(),

            // Extension point: a new source format implements its own steps and
            // adds another arm here.
            _ => throw new NotSupportedException($"No pipeline is registered for '{extension}' files.")
        };
    }

    private IImageProcessingPipeline CreatePdfPipeline() =>
        new StepPipeline(
            new IImageProcessingStep[]
            {
                new PdfRegionExtractionStep(pdfRegionExtractor),
                new DigitRecognitionStep(digitRecognizer),
                new PdfConfidenceEvaluationStep(confidenceOptions),
            },
            loggerFactory.CreateLogger<StepPipeline>());
}
