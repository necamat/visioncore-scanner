namespace VisionCore.Infrastructure.Implementations;

using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using VisionCore.Application.Abstractions;
using VisionCore.Application.Common;
using VisionCore.Domain.Models;

/// <summary>
/// Exports quiz results to an .xlsx workbook with a "Scans" sheet (one row per
/// processed sheet) and a "Standings" sheet (accumulated score per team), using
/// the ClosedXML library.
/// </summary>
public sealed class ClosedXmlExcelExporter(ILogger<ClosedXmlExcelExporter> logger) : IExcelExporter
{
    /// <summary>
    /// Name of the worksheet listing one row per processed sheet. The layout of
    /// this sheet is defined in <see cref="ScansSheetSchema"/> and shared with
    /// the reader that imports it back.
    /// </summary>
    public const string ScansSheetName = ScansSheetSchema.SheetName;

    /// <summary>Name of the worksheet listing accumulated score per team.</summary>
    public const string StandingsSheetName = "Standings";

    // The Standings sheet is written here and never read back, so its layout
    // stays local rather than in the shared schema.
    private const int StandingsHeaderRow = 1;
    private const int StandingsFirstDataRow = 2;
    private const int StandingsColumnTeam = 1;
    private const int StandingsColumnTotal = 2;

    /// <inheritdoc />
    public Task<Result> ExportAsync(QuizResult result, string outputPath, CancellationToken ct)
    {
        // The workbook is written to a sibling temp file and moved over the
        // target, so a crash mid-save can never leave a half-written file —
        // --finalize rewrites a workbook holding the operator's review work.
        var tempPath = outputPath + ".tmp";

        try
        {
            var scans = result.GetScans();
            var standings = result.GetStandings();
            var outputDirectory = Path.GetDirectoryName(outputPath);

            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            using var workbook = new XLWorkbook();
            var scansWorksheet = workbook.Worksheets.Add(ScansSheetSchema.SheetName);
            var standingsWorksheet = workbook.Worksheets.Add(StandingsSheetName);

            scansWorksheet.Cell(ScansSheetSchema.HeaderRow, ScansSheetSchema.ColumnRound).Value = ScansSheetSchema.HeaderRound;
            scansWorksheet.Cell(ScansSheetSchema.HeaderRow, ScansSheetSchema.ColumnSource).Value = ScansSheetSchema.HeaderSource;
            scansWorksheet.Cell(ScansSheetSchema.HeaderRow, ScansSheetSchema.ColumnTeamId).Value = ScansSheetSchema.HeaderTeamId;
            scansWorksheet.Cell(ScansSheetSchema.HeaderRow, ScansSheetSchema.ColumnScore).Value = ScansSheetSchema.HeaderScore;
            scansWorksheet.Cell(ScansSheetSchema.HeaderRow, ScansSheetSchema.ColumnConfidence).Value = ScansSheetSchema.HeaderConfidence;
            scansWorksheet.Cell(ScansSheetSchema.HeaderRow, ScansSheetSchema.ColumnStatus).Value = ScansSheetSchema.HeaderStatus;
            scansWorksheet.Cell(ScansSheetSchema.HeaderRow, ScansSheetSchema.ColumnFailure).Value = ScansSheetSchema.HeaderFailure;

            var scanRow = ScansSheetSchema.FirstDataRow;
            foreach (var scan in scans)
            {
                scansWorksheet.Cell(scanRow, ScansSheetSchema.ColumnRound).Value = scan.Round;
                scansWorksheet.Cell(scanRow, ScansSheetSchema.ColumnSource).Value = scan.SourcePath;
                scansWorksheet.Cell(scanRow, ScansSheetSchema.ColumnTeamId).Value = scan.TeamId;
                scansWorksheet.Cell(scanRow, ScansSheetSchema.ColumnScore).Value = scan.Score;
                scansWorksheet.Cell(scanRow, ScansSheetSchema.ColumnConfidence).Value = scan.Confidence;
                scansWorksheet.Cell(scanRow, ScansSheetSchema.ColumnStatus).Value = scan.Status.ToString();
                scansWorksheet.Cell(scanRow, ScansSheetSchema.ColumnFailure).Value = scan.FailureCode?.ToString();
                scanRow++;
            }

            standingsWorksheet.Cell(StandingsHeaderRow, StandingsColumnTeam).Value = "Team";
            standingsWorksheet.Cell(StandingsHeaderRow, StandingsColumnTotal).Value = "Total Score";

            var row = StandingsFirstDataRow;
            foreach (var standing in standings)
            {
                standingsWorksheet.Cell(row, StandingsColumnTeam).Value = standing.TeamId;
                standingsWorksheet.Cell(row, StandingsColumnTotal).Value = standing.TotalScore;
                row++;
            }

            scansWorksheet.Columns().AdjustToContents();
            standingsWorksheet.Columns().AdjustToContents();

            // SaveAs(string) infers the format from the extension, which a
            // ".tmp" suffix would break — the stream overload always writes xlsx.
            using (var stream = File.Create(tempPath))
            {
                workbook.SaveAs(stream);
            }

            File.Move(tempPath, outputPath, overwrite: true);
            return Task.FromResult(Result.Success());
        }
        catch (Exception ex)
        {
            TryDeleteTempFile(tempPath);
            logger.LogError(ex, "Excel export failed for path: {OutputPath}", outputPath);
            return Task.FromResult(Result.Failure($"Excel export failed: {ex.Message}"));
        }
    }

    private void TryDeleteTempFile(string tempPath)
    {
        try
        {
            File.Delete(tempPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not clean up temp export file {TempPath}", tempPath);
        }
    }
}
