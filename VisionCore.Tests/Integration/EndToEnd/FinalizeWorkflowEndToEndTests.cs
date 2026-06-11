using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VisionCore.Application.UseCases;
using VisionCore.Domain.Entities;
using VisionCore.Domain.Imaging.Evaluation;
using VisionCore.Domain.Models;
using VisionCore.Infrastructure.Implementations;
using Xunit;

namespace VisionCore.Tests.Integration.EndToEnd;

/// <summary>
/// End-to-end coverage of the human-review loop:
/// export a workbook with a NeedsReview row -> the operator corrects the score
/// and promotes the row in Excel -> <c>--finalize</c> re-imports the workbook
/// and regenerates the standings so the confirmed team now appears in them.
/// </summary>
public sealed class FinalizeWorkflowEndToEndTests : IDisposable
{
    private readonly string root;
    private readonly string workbookPath;
    private readonly ClosedXmlExcelExporter exporter =
        new(NullLogger<ClosedXmlExcelExporter>.Instance);

    public FinalizeWorkflowEndToEndTests()
    {
        root = Path.Combine(Path.GetTempPath(), "vc-finalize-e2e", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        workbookPath = Path.Combine(root, "results.xlsx");
    }

    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public async Task Finalize_Should_Move_A_Reviewed_Team_Into_The_Standings()
    {
        // A run that auto-accepted team 12 and flagged team 23 for review:
        // the standings list only team 12.
        var scanRun = new QuizResult();
        scanRun.AddScan(new SheetScanResult(1, "R1/a.pdf", 12, 75, 0.91, ReviewStatus.Accepted, null));
        scanRun.AddScan(new SheetScanResult(1, "R1/b.pdf", 23, 90, 0.72, ReviewStatus.NeedsReview, null));
        await exporter.ExportAsync(scanRun, workbookPath, CancellationToken.None);

        // The operator opens the workbook, corrects the score and promotes the row.
        using (var workbook = new XLWorkbook(workbookPath))
        {
            var scans = workbook.Worksheet(ClosedXmlExcelExporter.ScansSheetName);
            scans.Cell(3, 4).Value = 95; // corrected score
            scans.Cell(3, 6).Value = ReviewStatus.Accepted.ToString();
            workbook.Save();
        }

        var finalize = new FinalizeQuizResultUseCase(
            new ClosedXmlReviewedScansReader(NullLogger<ClosedXmlReviewedScansReader>.Instance),
            exporter,
            NullLogger<FinalizeQuizResultUseCase>.Instance);

        var result = await finalize.FinalizeAsync(workbookPath, CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error);

        using var finalized = new XLWorkbook(workbookPath);
        var standings = finalized.Worksheet(ClosedXmlExcelExporter.StandingsSheetName);
        standings.Cell(2, 1).GetValue<int>().Should().Be(23, "the confirmed team leads with 95");
        standings.Cell(2, 2).GetValue<int>().Should().Be(95);
        standings.Cell(3, 1).GetValue<int>().Should().Be(12);
        standings.Cell(3, 2).GetValue<int>().Should().Be(75);
    }

    [Fact]
    public async Task Finalize_Should_Leave_The_Workbook_Untouched_When_The_Review_Is_Malformed()
    {
        var scanRun = new QuizResult();
        scanRun.AddScan(new SheetScanResult(1, "R1/a.pdf", 12, 75, 0.91, ReviewStatus.Accepted, null));
        await exporter.ExportAsync(scanRun, workbookPath, CancellationToken.None);

        using (var workbook = new XLWorkbook(workbookPath))
        {
            var scans = workbook.Worksheet(ClosedXmlExcelExporter.ScansSheetName);
            scans.Cell(2, 6).Value = "Aproved"; // operator typo
            workbook.Save();
        }

        var originalBytes = await File.ReadAllBytesAsync(workbookPath);
        var finalize = new FinalizeQuizResultUseCase(
            new ClosedXmlReviewedScansReader(NullLogger<ClosedXmlReviewedScansReader>.Instance),
            exporter,
            NullLogger<FinalizeQuizResultUseCase>.Instance);

        var result = await finalize.FinalizeAsync(workbookPath, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Row 2");
        (await File.ReadAllBytesAsync(workbookPath)).Should().Equal(
            originalBytes, "a failed import must not rewrite the workbook");
    }
}
