using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VisionCore.Domain.Entities;
using VisionCore.Domain.Imaging.Evaluation;
using VisionCore.Domain.Models;
using VisionCore.Infrastructure.Implementations;
using Xunit;

namespace VisionCore.Tests.Unit.Infrastructure;

public sealed class ClosedXmlReviewedScansReaderTests : IDisposable
{
    private readonly string root;
    private readonly string workbookPath;
    private readonly ClosedXmlReviewedScansReader sut =
        new(NullLogger<ClosedXmlReviewedScansReader>.Instance);
    private readonly ClosedXmlExcelExporter exporter =
        new(NullLogger<ClosedXmlExcelExporter>.Instance);

    public ClosedXmlReviewedScansReaderTests()
    {
        root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        workbookPath = Path.Combine(root, "results.xlsx");
    }

    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public async Task ReadAsync_Should_Round_Trip_A_Workbook_Written_By_The_Exporter()
    {
        var original = new QuizResult();
        original.AddScan(new SheetScanResult(1, "R1/a.pdf", 12, 75, 0.91, ReviewStatus.Accepted, null));
        original.AddScan(new SheetScanResult(2, "R2/b.pdf", 23, 100, 0.72, ReviewStatus.NeedsReview, null));
        original.AddScan(new SheetScanResult(
            3, "R3/c.pdf", null, null, 0.10, ReviewStatus.Rejected, EvaluationFailureCode.LowConfidence));
        await exporter.ExportAsync(original, workbookPath, CancellationToken.None);

        var read = await sut.ReadAsync(workbookPath, CancellationToken.None);

        read.IsSuccess.Should().BeTrue(read.Error);
        read.Value!.GetScans().Should().BeEquivalentTo(original.GetScans());
    }

    [Fact]
    public async Task ReadAsync_Should_Pick_Up_An_Operator_Promotion_To_Accepted()
    {
        var original = new QuizResult();
        original.AddScan(new SheetScanResult(1, "R1/a.pdf", 12, 75, 0.72, ReviewStatus.NeedsReview, null));
        await exporter.ExportAsync(original, workbookPath, CancellationToken.None);
        PromoteRowToAccepted(row: 2, correctedScore: 80);

        var read = await sut.ReadAsync(workbookPath, CancellationToken.None);

        read.IsSuccess.Should().BeTrue(read.Error);
        var scan = read.Value!.GetScans().Single();
        scan.Status.Should().Be(ReviewStatus.Accepted);
        scan.Score.Should().Be(80);
        read.Value.GetStandings().Single().TotalScore.Should().Be(80);
    }

    [Fact]
    public async Task ReadAsync_Should_Fail_When_The_Workbook_Is_Missing()
    {
        var read = await sut.ReadAsync(Path.Combine(root, "missing.xlsx"), CancellationToken.None);

        read.IsFailure.Should().BeTrue();
        read.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task ReadAsync_Should_Fail_When_The_Scans_Sheet_Is_Missing()
    {
        using (var workbook = new XLWorkbook())
        {
            workbook.Worksheets.Add("SomethingElse");
            workbook.SaveAs(workbookPath);
        }

        var read = await sut.ReadAsync(workbookPath, CancellationToken.None);

        read.IsFailure.Should().BeTrue();
        read.Error.Should().Contain(ClosedXmlExcelExporter.ScansSheetName);
    }

    [Fact]
    public async Task ReadAsync_Should_Fail_With_The_Row_Position_For_A_Mistyped_Status()
    {
        await ExportSingleAcceptedScan();
        MutateCell(row: 2, column: 6, value: "Acepted");

        var read = await sut.ReadAsync(workbookPath, CancellationToken.None);

        read.IsFailure.Should().BeTrue();
        read.Error.Should().Contain("Row 2").And.Contain("Acepted");
    }

    [Fact]
    public async Task ReadAsync_Should_Fail_When_An_Accepted_Row_Lacks_A_Score()
    {
        await ExportSingleAcceptedScan();
        MutateCell(row: 2, column: 4, value: "");

        var read = await sut.ReadAsync(workbookPath, CancellationToken.None);

        read.IsFailure.Should().BeTrue();
        read.Error.Should().Contain("Row 2").And.Contain("Accepted");
    }

    [Fact]
    public async Task ReadAsync_Should_Fail_When_A_Score_Is_Not_A_Number()
    {
        await ExportSingleAcceptedScan();
        MutateCell(row: 2, column: 4, value: "seventy five");

        var read = await sut.ReadAsync(workbookPath, CancellationToken.None);

        read.IsFailure.Should().BeTrue();
        read.Error.Should().Contain("Row 2").And.Contain("Score");
    }

    private async Task ExportSingleAcceptedScan()
    {
        var original = new QuizResult();
        original.AddScan(new SheetScanResult(1, "R1/a.pdf", 12, 75, 0.91, ReviewStatus.Accepted, null));
        await exporter.ExportAsync(original, workbookPath, CancellationToken.None);
    }

    private void PromoteRowToAccepted(int row, int correctedScore)
    {
        using var workbook = new XLWorkbook(workbookPath);
        var sheet = workbook.Worksheet(ClosedXmlExcelExporter.ScansSheetName);
        sheet.Cell(row, 4).Value = correctedScore;
        sheet.Cell(row, 6).Value = ReviewStatus.Accepted.ToString();
        workbook.Save();
    }

    private void MutateCell(int row, int column, string value)
    {
        using var workbook = new XLWorkbook(workbookPath);
        var sheet = workbook.Worksheet(ClosedXmlExcelExporter.ScansSheetName);
        sheet.Cell(row, column).Value = value;
        workbook.Save();
    }
}
