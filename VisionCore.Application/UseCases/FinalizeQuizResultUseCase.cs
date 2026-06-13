namespace VisionCore.Application.UseCases;

using Microsoft.Extensions.Logging;
using VisionCore.Application.Abstractions;
using VisionCore.Application.Common;

/// <summary>
/// Completes the human-review loop: reads a reviewed results workbook back in
/// (corrected values, NeedsReview rows promoted to Accepted) and re-exports it
/// so the standings reflect the confirmed scans. The workbook is rewritten in
/// place — the Scans sheet is normalized and the Standings sheet regenerated.
/// </summary>
public sealed class FinalizeQuizResultUseCase(
    IReviewedScansReader reviewedScansReader,
    IExcelExporter excelExporter,
    ILogger<FinalizeQuizResultUseCase> logger)
{
    public async Task<Result> FinalizeAsync(string workbookPath, CancellationToken ct)
    {
        logger.LogInformation("Finalizing reviewed workbook {Workbook}", workbookPath);

        var reviewed = await reviewedScansReader.ReadAsync(workbookPath, ct);
        if (reviewed.IsFailure)
        {
            logger.LogError("Review import failed: {Error}", reviewed.Error);
            return Result.Failure(reviewed.Error!);
        }

        var reviewedResult = reviewed.Value!;
        var export = await excelExporter.ExportAsync(reviewedResult, workbookPath, ct);
        if (export.IsFailure)
        {
            logger.LogError("Re-export failed: {Error}", export.Error);
            return export;
        }

        logger.LogInformation(
            "Finalized {Scans} reviewed scans into {Workbook}",
            reviewedResult.GetScans().Count,
            workbookPath);
        return Result.Success();
    }
}
