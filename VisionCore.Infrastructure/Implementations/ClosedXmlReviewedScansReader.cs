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
/// carry both a team id and a score — and the first malformed row fails the
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

    public Task<Result<QuizResult>> ReadAsync(string workbookPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!File.Exists(workbookPath))
        {
            return Task.FromResult(Result<QuizResult>.Failure($"Workbook not found: {workbookPath}"));
        }

        try
        {
            using var workbook = new XLWorkbook(workbookPath);
            if (!workbook.TryGetWorksheet(ClosedXmlExcelExporter.ScansSheetName, out var scansSheet))
            {
                return Task.FromResult(Result<QuizResult>.Failure(
                    $"Workbook has no '{ClosedXmlExcelExporter.ScansSheetName}' sheet: {workbookPath}"));
            }

            return Task.FromResult(ReadScans(scansSheet));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or FormatException)
        {
            logger.LogError(ex, "Could not read reviewed workbook {Workbook}", workbookPath);
            return Task.FromResult(Result<QuizResult>.Failure($"Could not read workbook: {ex.Message}"));
        }
    }

    private static Result<QuizResult> ReadScans(IXLWorksheet scansSheet)
    {
        var result = new QuizResult();
        var lastRow = scansSheet.LastRowUsed()?.RowNumber() ?? 0;

        for (var row = FirstDataRow; row <= lastRow; row++)
        {
            var scan = ReadScanRow(scansSheet, row);
            if (scan.IsFailure)
            {
                return Result<QuizResult>.Failure(scan.Error!);
            }

            result.AddScan(scan.Value!);
        }

        return Result<QuizResult>.Success(result);
    }

    private static Result<SheetScanResult> ReadScanRow(IXLWorksheet sheet, int row)
    {
        if (!sheet.Cell(row, ColumnRound).TryGetValue<int>(out var round))
        {
            return Result<SheetScanResult>.Failure($"Row {row}: Round must be a whole number.");
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

        if (status == ReviewStatus.Accepted && (teamId is null || score is null))
        {
            return Result<SheetScanResult>.Failure(
                $"Row {row}: an Accepted row must have both Team ID and Score.");
        }

        var confidence = sheet.Cell(row, ColumnConfidence).TryGetValue<double>(out var parsedConfidence)
            ? parsedConfidence
            : 0d;

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

    private static int? ReadOptionalInt(IXLCell cell) =>
        cell.TryGetValue<int>(out var value) ? value : null;
}
