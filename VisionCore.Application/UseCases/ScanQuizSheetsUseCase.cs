namespace VisionCore.Application.UseCases;

using Microsoft.Extensions.Logging;
using VisionCore.Application.Abstractions;
using VisionCore.Application.Common;
using VisionCore.Application.Mapping;
using VisionCore.Domain.Models;

/// <summary>
/// Scans every source document supplied by the <see cref="IScanSourceProvider"/>
/// through the format-appropriate pipeline and collects the per-sheet results
/// into a <see cref="QuizResult"/>. Resilient per file — a single sheet that
/// fails is recorded as rejected and the run continues. Exporting the result is
/// the caller's responsibility.
/// </summary>
public sealed class ScanQuizSheetsUseCase(
    IScanSourceProvider sourceProvider,
    IPipelineFactory pipelineFactory,
    ILogger<ScanQuizSheetsUseCase> logger)
{
    public async Task<Result<QuizResult>> ScanAsync(string root, CancellationToken ct)
    {
        logger.LogInformation("Scanning sources under {Root}", root);

        if (!Directory.Exists(root))
        {
            logger.LogWarning("Root folder does not exist: {Root}", root);
            return Result<QuizResult>.Failure("Root folder does not exist");
        }

        IReadOnlyList<ScanSource> sources;
        try
        {
            sources = sourceProvider.GetSources(root).ToList();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Failed to enumerate scan sources under {Root}", root);
            return Result<QuizResult>.Failure(ex.Message);
        }

        var quizResult = new QuizResult();

        foreach (var source in sources)
        {
            ct.ThrowIfCancellationRequested();
            quizResult.AddScan(await ScanSourceAsync(source, ct));
        }

        return Result<QuizResult>.Success(quizResult);
    }

    private async Task<Domain.Entities.SheetScanResult> ScanSourceAsync(ScanSource source, CancellationToken ct)
    {
        logger.LogInformation("Processing {File}", source.SourcePath);

        try
        {
            var pipeline = pipelineFactory.CreateForSource(source.SourcePath);
            ct.ThrowIfCancellationRequested();
            var result = await pipeline.ProcessAsync(source.SourcePath, ct);

            var scan = result.ToScanResult(source.Round, source.SourcePath);
            logger.LogInformation("Processed result {@Result}", scan);
            return scan;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error while processing {File}", source.SourcePath);
            return SheetScanResultMapper.Rejected(source.Round, source.SourcePath);
        }
    }
}
