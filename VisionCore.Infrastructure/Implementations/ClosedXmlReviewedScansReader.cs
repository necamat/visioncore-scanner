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
/// <see cref="ClosedXmlExcelExporter"/>, laid out per <see cref="ScansSheetSchema"/>)
/// back into a <see cref="QuizResult"/>. The header row is checked first so a
/// foreign or reordered workbook is rejected rather than misread; then every
/// data row is validated — status must be a known value, an Accepted row must
/// carry both a team id and a score, numbers must sit inside the form's ranges
/// and no sheet may appear twice — and the first malformed row fails the import
/// with its position, so a typo in the review never silently corrupts the
/// standings.
/// </summary>
public sealed class ClosedXmlReviewedScansReader(ILogger<ClosedXmlReviewedScansReader> logger)
    : IReviewedScansReader
{
    // The printed form reads the team id from two digit cells and the score
    // from three (see FormRegion), so the largest value each can legitimately
    // hold is all-nines (99 / 999). The bounds are derived from the cell counts
    // so they stay in step if the form ever changes; a review that produces
    // anything larger is a typo the import must catch.
    private const int TeamIdDigitCount = 2;
    private const int ScoreDigitCount = 3;
    private static readonly int MaxTeamId = AllNines(TeamIdDigitCount);
    private static readonly int MaxScore = AllNines(ScoreDigitCount);

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
            if (!workbook.TryGetWorksheet(ScansSheetSchema.SheetName, out var scansSheet))
            {
                return Task.FromResult(Result<QuizResult>.Failure(
                    $"Workbook has no '{ScansSheetSchema.SheetName}' sheet: {workbookPath}"));
            }

            return Task.FromResult(ReadScans(scansSheet));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or FormatException)
        {
            // Boundary adapter over an operator-supplied file: a corrupt or
            // non-xlsx workbook surfaces as FileFormatException (a FormatException)
            // or an IO/parse error. These become an import-failed Result;
            // anything else is a programmer error and is left to propagate.
            logger.LogError(ex, "Could not read reviewed workbook {Workbook}", workbookPath);
            return Task.FromResult(Result<QuizResult>.Failure($"Could not read workbook: {ex.Message}"));
        }
    }

    private static Result<QuizResult> ReadScans(IXLWorksheet scansSheet)
    {
        var header = ValidateHeader(scansSheet);
        if (header.IsFailure)
        {
            return Result<QuizResult>.Failure(header.Error!);
        }

        var result = new QuizResult();
        var seenSources = new Dictionary<(int Round, string SourcePath), int>();
        var lastRow = scansSheet.LastRowUsed()?.RowNumber() ?? 0;

        for (var row = ScansSheetSchema.FirstDataRow; row <= lastRow; row++)
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

            var duplicate = CheckForDuplicate(seenSources, scan.Value!, row);
            if (duplicate.IsFailure)
            {
                return Result<QuizResult>.Failure(duplicate.Error!);
            }

            result.AddScan(scan.Value!);
        }

        return Result<QuizResult>.Success(result);
    }

    /// <summary>
    /// Confirms the sheet carries the layout this reader expects before any
    /// column position is trusted — a workbook not produced by the exporter (or
    /// one whose columns the operator reordered) is rejected up front rather
    /// than read against the wrong columns.
    /// </summary>
    private static Result ValidateHeader(IXLWorksheet sheet)
    {
        for (var index = 0; index < ScansSheetSchema.HeaderLabels.Count; index++)
        {
            var column = ScansSheetSchema.ColumnRound + index;
            var expected = ScansSheetSchema.HeaderLabels[index];
            var actual = sheet.Cell(ScansSheetSchema.HeaderRow, column).GetString().Trim();
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure(
                    $"Unexpected column layout: column {column} header is '{actual}', expected '{expected}'. " +
                    "The workbook must be one exported by VisionCore.");
            }
        }

        return Result.Success();
    }

    private static Result<SheetScanResult> ReadScanRow(IXLWorksheet sheet, int row)
    {
        if (!sheet.Cell(row, ScansSheetSchema.ColumnRound).TryGetValue<int>(out var round) || round < 1)
        {
            return Result<SheetScanResult>.Failure(
                $"Row {row}: Round '{sheet.Cell(row, ScansSheetSchema.ColumnRound).GetString()}' " +
                "must be a positive whole number.");
        }

        var sourcePath = sheet.Cell(row, ScansSheetSchema.ColumnSource).GetString();

        var statusText = sheet.Cell(row, ScansSheetSchema.ColumnStatus).GetString();
        if (!Enum.TryParse<ReviewStatus>(statusText, ignoreCase: true, out var status))
        {
            return Result<SheetScanResult>.Failure(
                $"Row {row}: Status '{statusText}' is not one of " +
                $"{string.Join(", ", Enum.GetNames<ReviewStatus>())}.");
        }

        var teamId = ReadOptionalInt(sheet.Cell(row, ScansSheetSchema.ColumnTeamId));
        var score = ReadOptionalInt(sheet.Cell(row, ScansSheetSchema.ColumnScore));
        if (teamId is null && !sheet.Cell(row, ScansSheetSchema.ColumnTeamId).IsEmpty())
        {
            return Result<SheetScanResult>.Failure($"Row {row}: Team ID must be a whole number or empty.");
        }

        if (score is null && !sheet.Cell(row, ScansSheetSchema.ColumnScore).IsEmpty())
        {
            return Result<SheetScanResult>.Failure($"Row {row}: Score must be a whole number or empty.");
        }

        // Lifted comparisons: an empty cell (null) is neither below 0 nor above
        // the max, so it passes here and is caught by the Accepted-row rule.
        if (teamId < 0 || teamId > MaxTeamId)
        {
            return Result<SheetScanResult>.Failure(
                $"Row {row}: Team ID {teamId} is outside the form's 0-{MaxTeamId} range.");
        }

        if (score < 0 || score > MaxScore)
        {
            return Result<SheetScanResult>.Failure(
                $"Row {row}: Score {score} is outside the form's 0-{MaxScore} range.");
        }

        if (status == ReviewStatus.Accepted && (teamId is null || score is null))
        {
            return Result<SheetScanResult>.Failure(
                $"Row {row}: an Accepted row must have both Team ID and Score.");
        }

        var confidenceCell = sheet.Cell(row, ScansSheetSchema.ColumnConfidence);
        var confidence = 0d;
        if (!confidenceCell.IsEmpty() && !confidenceCell.TryGetValue(out confidence))
        {
            return Result<SheetScanResult>.Failure(
                $"Row {row}: Confidence '{confidenceCell.GetString()}' must be a number or empty.");
        }

        var failure = ReadOptionalFailureCode(sheet.Cell(row, ScansSheetSchema.ColumnFailure), row);
        if (failure.IsFailure)
        {
            return Result<SheetScanResult>.Failure(failure.Error!);
        }

        return Result<SheetScanResult>.Success(
            new SheetScanResult(round, sourcePath, teamId, score, confidence, status, failure.Value));
    }

    /// <summary>
    /// Flags a row that repeats an earlier row's (round, source) pair — a
    /// copy-paste slip in the review would otherwise count one sheet's score
    /// twice in the standings. Rows with no source path are operator-added
    /// sheets (e.g. one the scanner missed) and are intentionally not deduped:
    /// two such rows for the same team are legitimate distinct entries.
    /// </summary>
    private static Result CheckForDuplicate(
        Dictionary<(int Round, string SourcePath), int> seenSources, SheetScanResult scan, int row)
    {
        if (string.IsNullOrWhiteSpace(scan.SourcePath))
        {
            return Result.Success();
        }

        var key = (scan.Round, scan.SourcePath);
        if (seenSources.TryGetValue(key, out var firstRow))
        {
            return Result.Failure(
                $"Row {row}: duplicates row {firstRow} (round {scan.Round}, '{scan.SourcePath}') — " +
                "a sheet listed twice would count twice in the standings.");
        }

        seenSources.Add(key, row);
        return Result.Success();
    }

    private static bool IsRowEmpty(IXLWorksheet sheet, int row)
    {
        for (var column = ScansSheetSchema.ColumnRound; column <= ScansSheetSchema.LastColumn; column++)
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

    /// <summary>The largest value <paramref name="digits"/> decimal cells can hold (e.g. 3 → 999).</summary>
    private static int AllNines(int digits)
    {
        var max = 0;
        for (var index = 0; index < digits; index++)
        {
            max = (max * 10) + 9;
        }

        return max;
    }

    private static Result<EvaluationFailureCode?> ReadOptionalFailureCode(IXLCell cell, int row)
    {
        var failureText = cell.GetString();
        if (string.IsNullOrWhiteSpace(failureText))
        {
            return Result<EvaluationFailureCode?>.Success(null);
        }

        if (!Enum.TryParse<EvaluationFailureCode>(failureText, ignoreCase: true, out var parsed))
        {
            return Result<EvaluationFailureCode?>.Failure(
                $"Row {row}: Failure '{failureText}' is not a known failure code.");
        }

        return Result<EvaluationFailureCode?>.Success(parsed);
    }
}
