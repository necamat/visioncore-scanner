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
    public async Task Finalize_Should_Recover_The_Ground_Truth_After_Reviewing_A_Hard_Scan_Run()
    {
        // The whole loop over hard inputs: confusable digit pairs scanned
        // clean, plus two sheets degraded enough that the pipeline may flag
        // or reject them. The operator (this test) reviews the workbook
        // against the ground truth and finalizes — whatever each sheet's
        // automatic outcome was, the standings must land exactly on the
        // ground truth.
        var groundTruth = new Dictionary<string, (int TeamId, int Score)>
        {
            ["team80.pdf"] = (80, 808),
            ["team69.pdf"] = (69, 696),
            ["team38.pdf"] = (38, 583),
            ["team17.pdf"] = (17, 171)
        };

        var inputRoot = Path.Combine(root, "input");
        var regions = SyntheticPdfDatasetBuilder.DefaultRegions();
        var builder = new SyntheticPdfDatasetBuilder();
        builder.BuildSheet(Path.Combine(inputRoot, "R1", "team80.pdf"), 80, 808);
        builder.BuildSheet(Path.Combine(inputRoot, "R1", "team69.pdf"), 69, 696);
        builder.BuildSheet(Path.Combine(inputRoot, "R2", "team38.pdf"), 38, 583, new SheetDistortion
        {
            NoiseSpeckles = 3000,
            InkIntensity = 130,
            JpegQuality = 45,
            DigitOffsetX = 8,
            DigitOffsetY = 6
        });
        builder.BuildSheet(Path.Combine(inputRoot, "R2", "team17.pdf"), 17, 171, new SheetDistortion
        {
            NoiseSpeckles = 400,
            InkIntensity = 80,
            JpegQuality = 75,
            DigitOffsetX = 3,
            DigitOffsetY = 2,
            KeepSpecklesOffDigits = true
        });

        var scan = await PdfTestPipeline.CreateScanUseCase(regions)
            .ScanAsync(inputRoot, CancellationToken.None);
        scan.IsSuccess.Should().BeTrue(scan.Error);
        (await exporter.ExportAsync(scan.Value!, workbookPath, CancellationToken.None))
            .IsSuccess.Should().BeTrue();

        ReviewWorkbookAgainst(groundTruth);

        var finalize = new FinalizeQuizResultUseCase(
            new ClosedXmlReviewedScansReader(NullLogger<ClosedXmlReviewedScansReader>.Instance),
            exporter,
            NullLogger<FinalizeQuizResultUseCase>.Instance);

        var result = await finalize.FinalizeAsync(workbookPath, CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error);

        using var finalized = new XLWorkbook(workbookPath);
        var standings = finalized.Worksheet(ClosedXmlExcelExporter.StandingsSheetName);
        var expectedOrder = new[]
        {
            (Team: 80, Total: 808),
            (Team: 69, Total: 696),
            (Team: 38, Total: 583),
            (Team: 17, Total: 171)
        };
        for (var position = 0; position < expectedOrder.Length; position++)
        {
            var row = 2 + position;
            standings.Cell(row, 1).GetValue<int>().Should().Be(expectedOrder[position].Team);
            standings.Cell(row, 2).GetValue<int>().Should().Be(expectedOrder[position].Total);
        }

        standings.Cell(2 + expectedOrder.Length, 1).IsEmpty()
            .Should().BeTrue("exactly four teams are expected in the standings");
    }

    /// <summary>
    /// Plays the operator: every row the pipeline did not auto-accept gets the
    /// ground-truth values and is promoted to Accepted; every row it did
    /// auto-accept must already hold the ground truth — that is the hard-input
    /// guarantee the recognition suite enforces.
    /// </summary>
    private void ReviewWorkbookAgainst(IReadOnlyDictionary<string, (int TeamId, int Score)> groundTruth)
    {
        using var workbook = new XLWorkbook(workbookPath);
        var scans = workbook.Worksheet(ClosedXmlExcelExporter.ScansSheetName);
        var lastRow = scans.LastRowUsed()!.RowNumber();
        lastRow.Should().Be(1 + groundTruth.Count, "every built sheet must appear in the workbook");

        for (var row = 2; row <= lastRow; row++)
        {
            var fileName = Path.GetFileName(scans.Cell(row, 2).GetString());
            var (teamId, score) = groundTruth[fileName];

            if (scans.Cell(row, 6).GetString() == nameof(ReviewStatus.Accepted))
            {
                scans.Cell(row, 3).GetValue<int>().Should().Be(
                    teamId, $"row {row} ({fileName}) was auto-accepted");
                scans.Cell(row, 4).GetValue<int>().Should().Be(
                    score, $"row {row} ({fileName}) was auto-accepted");
                continue;
            }

            scans.Cell(row, 3).Value = teamId;
            scans.Cell(row, 4).Value = score;
            scans.Cell(row, 6).Value = nameof(ReviewStatus.Accepted);
            scans.Cell(row, 7).Value = string.Empty;
        }

        workbook.Save();
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
