namespace VisionCore.Application.Abstractions;

using VisionCore.Application.Common;
using VisionCore.Domain.Models;

/// <summary>
/// Exports the processed quiz results (per-sheet scans and team standings)
/// to an Excel workbook.
/// </summary>
public interface IExcelExporter
{
    /// <summary>
    /// Writes the given <see cref="QuizResult"/> to an Excel workbook at
    /// <paramref name="outputPath"/>.
    /// </summary>
    Task<Result> ExportAsync(QuizResult result, string outputPath, CancellationToken ct);
}
