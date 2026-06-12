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
    /// <summary>Name of the worksheet listing one row per processed sheet.</summary>
    public const string ScansSheetName = "Scans";

    /// <summary>Name of the worksheet listing accumulated score per team.</summary>
    public const string StandingsSheetName = "Standings";

    private const int HeaderRow = 1;
    private const int FirstDataRow = 2;

    private const int ColumnRound = 1;
    private const int ColumnSource = 2;
    private const int ColumnTeamId = 3;
    private const int ColumnScore = 4;
    private const int ColumnConfidence = 5;
    private const int ColumnStatus = 6;
    private const int ColumnFailure = 7;

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
            var scansWorksheet = workbook.Worksheets.Add(ScansSheetName);
            var standingsWorksheet = workbook.Worksheets.Add(StandingsSheetName);

            scansWorksheet.Cell(HeaderRow, ColumnRound).Value = "Round";
            scansWorksheet.Cell(HeaderRow, ColumnSource).Value = "Source Path";
            scansWorksheet.Cell(HeaderRow, ColumnTeamId).Value = "Team ID";
            scansWorksheet.Cell(HeaderRow, ColumnScore).Value = "Score";
            scansWorksheet.Cell(HeaderRow, ColumnConfidence).Value = "Confidence";
            scansWorksheet.Cell(HeaderRow, ColumnStatus).Value = "Status";
            scansWorksheet.Cell(HeaderRow, ColumnFailure).Value = "Failure";

            var scanRow = FirstDataRow;
            foreach (var scan in scans)
            {
                scansWorksheet.Cell(scanRow, ColumnRound).Value = scan.Round;
                scansWorksheet.Cell(scanRow, ColumnSource).Value = scan.SourcePath;
                scansWorksheet.Cell(scanRow, ColumnTeamId).Value = scan.TeamId;
                scansWorksheet.Cell(scanRow, ColumnScore).Value = scan.Score;
                scansWorksheet.Cell(scanRow, ColumnConfidence).Value = scan.Confidence;
                scansWorksheet.Cell(scanRow, ColumnStatus).Value = scan.Status.ToString();
                scansWorksheet.Cell(scanRow, ColumnFailure).Value = scan.FailureCode?.ToString();
                scanRow++;
            }

            standingsWorksheet.Cell(HeaderRow, StandingsColumnTeam).Value = "Team";
            standingsWorksheet.Cell(HeaderRow, StandingsColumnTotal).Value = "Total Score";

            var row = FirstDataRow;
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
