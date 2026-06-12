namespace VisionCore.Infrastructure.Implementations;

using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using VisionCore.Application.Abstractions;
using VisionCore.Application.Common;
using VisionCore.Domain.Entities;
using VisionCore.Domain.Imaging.Evaluation;
using VisionCore.Domain.Models;

/// <summary>
/// Reads the "Scans" sheet of a results workbook (as written by
/// <see cref="ClosedXmlExcelExporter"/>) back into a <see cref="QuizResult"/>.
/// Every row is validated — status must be a known value, an Accepted row must
/// carry both a team id and a score, numbers must sit inside the form's ranges
/// and no sheet may appear twice — and the first malformed row fails the
/// import with its position, so a typo in the review never silently corrupts
/// the standings.
/// </summary>
public sealed class ClosedXmlReviewedScansReader(ILogger<ClosedXmlReviewedScansReader> logger)
    : IReviewedScansReader
{
    private const int FirstDataRow = 2;

    private const int ColumnRound = 1;
    private const int ColumnSource = 2;
    private const int ColumnTeamId = 3;
    private const int ColumnScore = 4;
    private const int ColumnConfidence = 5;
    private const int ColumnStatus = 6;
    private const int ColumnFailure = 7;

    // The printed form has two team-id digit cells and three score digit cells
    // (see FormRegion), so a review can never legitimately produce values
    // outside these ranges — anything larger is a typo the import must catch.
    private const int MaxTeamId = 99;
    private const int MaxScore = 999;

    public Task<Result<QuizResult>> ReadAsync(string workbookPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!File.Exists(workbookPath))
        {
            return Task.FromResult(Result<QuizResult>.Failure($"Workbook not found: {workbookPath}"));
        }

        try
        {
            // The stream is opened here rather than handed to XLWorkbook as a
            // path: when the constructor throws on a corrupt file, its
            // internally opened stream is never disposed and the file stays
            // locked — this way the handle is released either way.
            using var stream = File.OpenRead(workbookPath);
            using var workbook = new XLWorkbook(stream);
            if (!workbook.TryGetWorksheet(ClosedXmlExcelExporter.ScansSheetName, out var scansSheet))
            {
                return Task.FromResult(Result<QuizResult>.Failure(
                    $"Workbook has no '{ClosedXmlExcelExporter.ScansSheetName}' sheet: {workbookPath}"));
            }

            return Task.FromResult(ReadScans(scansSheet));
        }
        catch (Exception ex)
        {
            // Boundary adapter: a corrupt or non-xlsx file surfaces from the
            // OpenXML stack as several unrelated exception types, so the
            // import-failed result is the catch-all here (the exporter draws
            // the same boundary).
            logger.LogError(ex, "Could not read reviewed workbook {Workbook}", workbookPath);
            return Task.FromResult(Result<QuizResult>.Failure($"Could not read workbook: {ex.Message}"));
        }
    }

    private static Result<QuizResult> ReadScans(IXLWorksheet scansSheet)
    {
        var result = new QuizResult();
        var seenSources = new Dictionary<(int Round, string SourcePath), int>();
        var lastRow = scansSheet.LastRowUsed()?.RowNumber() ?? 0;

        for (var row = FirstDataRow; row <= lastRow; row++)
        {
            // A row cleared out in the review is the same operator action as a
            // deleted one (which leaves no trace at all), so it is skipped
            // rather than failed.
            if (IsRowEmpty(scansSheet, row))
            {
                continue;
            }

            var scan = ReadScanRow(scansSheet, row);
            if (scan.IsFailure)
            {
                return Result<QuizResult>.Failure(scan.Error!);
            }

            var key = (scan.Value!.Round, scan.Value.SourcePath);
            if (!string.IsNullOrWhiteSpace(key.SourcePath))
            {
                if (seenSources.TryGetValue(key, out var firstRow))
                {
                    return Result<QuizResult>.Failure(
                        $"Row {row}: duplicates row {firstRow} (round {key.Round}, " +
                        $"'{key.SourcePath}') — a sheet listed twice would count twice in the standings.");
                }

                seenSources.Add(key, row);
            }

            result.AddScan(scan.Value!);
        }

        return Result<QuizResult>.Success(result);
    }

    private static Result<SheetScanResult> ReadScanRow(IXLWorksheet sheet, int row)
    {
        if (!sheet.Cell(row, ColumnRound).TryGetValue<int>(out var round) || round < 1)
        {
            return Result<SheetScanResult>.Failure(
                $"Row {row}: Round '{sheet.Cell(row, ColumnRound).GetString()}' " +
                "must be a positive whole number.");
        }

        var sourcePath = sheet.Cell(row, ColumnSource).GetString();

        var statusText = sheet.Cell(row, ColumnStatus).GetString();
        if (!Enum.TryParse<ReviewStatus>(statusText, ignoreCase: true, out var status))
        {
            return Result<SheetScanResult>.Failure(
                $"Row {row}: Status '{statusText}' is not one of " +
                $"{string.Join(", ", Enum.GetNames<ReviewStatus>())}.");
        }

        var teamId = ReadOptionalInt(sheet.Cell(row, ColumnTeamId));
        var score = ReadOptionalInt(sheet.Cell(row, ColumnScore));
        if (teamId is null && !sheet.Cell(row, ColumnTeamId).IsEmpty())
        {
            return Result<SheetScanResult>.Failure($"Row {row}: Team ID must be a whole number or empty.");
        }

        if (score is null && !sheet.Cell(row, ColumnScore).IsEmpty())
        {
            return Result<SheetScanResult>.Failure($"Row {row}: Score must be a whole number or empty.");
        }

        if (teamId is < 0 or > MaxTeamId)
        {
            return Result<SheetScanResult>.Failure(
                $"Row {row}: Team ID {teamId} is outside the form's 0-{MaxTeamId} range.");
        }

        if (score is < 0 or > MaxScore)
        {
            return Result<SheetScanResult>.Failure(
                $"Row {row}: Score {score} is outside the form's 0-{MaxScore} range.");
        }

        if (status == ReviewStatus.Accepted && (teamId is null || score is null))
        {
            return Result<SheetScanResult>.Failure(
                $"Row {row}: an Accepted row must have both Team ID and Score.");
        }

        var confidenceCell = sheet.Cell(row, ColumnConfidence);
        var confidence = 0d;
        if (!confidenceCell.IsEmpty() && !confidenceCell.TryGetValue(out confidence))
        {
            return Result<SheetScanResult>.Failure(
                $"Row {row}: Confidence '{confidenceCell.GetString()}' must be a number or empty.");
        }

        EvaluationFailureCode? failureCode = null;
        var failureText = sheet.Cell(row, ColumnFailure).GetString();
        if (!string.IsNullOrWhiteSpace(failureText))
        {
            if (!Enum.TryParse<EvaluationFailureCode>(failureText, ignoreCase: true, out var parsedFailure))
            {
                return Result<SheetScanResult>.Failure(
                    $"Row {row}: Failure '{failureText}' is not a known failure code.");
            }

            failureCode = parsedFailure;
        }

        return Result<SheetScanResult>.Success(
            new SheetScanResult(round, sourcePath, teamId, score, confidence, status, failureCode));
    }

    private static bool IsRowEmpty(IXLWorksheet sheet, int row)
    {
        for (var column = ColumnRound; column <= ColumnFailure; column++)
        {
            if (!sheet.Cell(row, column).IsEmpty())
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads a cell as a whole number: blank yields null, numeric text typed
    /// into a text-formatted cell converts like a number, and a fractional
    /// value (75.5) yields null — rounding a typo would silently change a
    /// score. Sign and magnitude are the range checks' concern.
    /// </summary>
    private static int? ReadOptionalInt(IXLCell cell)
    {
        if (cell.IsEmpty() || !cell.TryGetValue<double>(out var value))
        {
            return null;
        }

        return value == Math.Floor(value) && value is >= int.MinValue and <= int.MaxValue
            ? (int)value
            : null;
    }
}
