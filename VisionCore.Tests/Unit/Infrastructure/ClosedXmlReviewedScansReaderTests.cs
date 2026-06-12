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

    [Fact]
    public async Task ReadAsync_Should_Fail_Cleanly_When_The_File_Is_Not_A_Workbook()
    {
        await File.WriteAllTextAsync(workbookPath, "this is not an xlsx file");

        var read = await sut.ReadAsync(workbookPath, CancellationToken.None);

        read.IsFailure.Should().BeTrue();
        read.Error.Should().Contain("Could not read workbook");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    [InlineData(250)]
    public async Task ReadAsync_Should_Fail_When_The_Team_Id_Is_Outside_The_Form_Range(int teamId)
    {
        await ExportSingleAcceptedScan();
        MutateCell(row: 2, column: 3, value: teamId.ToString());

        var read = await sut.ReadAsync(workbookPath, CancellationToken.None);

        read.IsFailure.Should().BeTrue();
        read.Error.Should().Contain("Row 2").And.Contain("0-99");
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(1000)]
    [InlineData(7555)]
    public async Task ReadAsync_Should_Fail_When_The_Score_Is_Outside_The_Form_Range(int score)
    {
        await ExportSingleAcceptedScan();
        MutateCell(row: 2, column: 4, value: score.ToString());

        var read = await sut.ReadAsync(workbookPath, CancellationToken.None);

        read.IsFailure.Should().BeTrue();
        read.Error.Should().Contain("Row 2").And.Contain("0-999");
    }

    [Fact]
    public async Task ReadAsync_Should_Fail_When_The_Score_Is_Not_A_Whole_Number()
    {
        await ExportSingleAcceptedScan();
        MutateNumericCell(row: 2, column: 4, value: 75.5);

        var read = await sut.ReadAsync(workbookPath, CancellationToken.None);

        read.IsFailure.Should().BeTrue();
        read.Error.Should().Contain("Row 2").And.Contain("whole number");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("first")]
    public async Task ReadAsync_Should_Fail_When_The_Round_Is_Not_A_Positive_Whole_Number(string round)
    {
        await ExportSingleAcceptedScan();
        MutateCell(row: 2, column: 1, value: round);

        var read = await sut.ReadAsync(workbookPath, CancellationToken.None);

        read.IsFailure.Should().BeTrue();
        read.Error.Should().Contain("Row 2").And.Contain(round);
    }

    [Fact]
    public async Task ReadAsync_Should_Fail_When_The_Confidence_Is_Not_A_Number()
    {
        await ExportSingleAcceptedScan();
        MutateCell(row: 2, column: 5, value: "high");

        var read = await sut.ReadAsync(workbookPath, CancellationToken.None);

        read.IsFailure.Should().BeTrue();
        read.Error.Should().Contain("Row 2").And.Contain("high");
    }

    [Fact]
    public async Task ReadAsync_Should_Default_An_Empty_Confidence_To_Zero()
    {
        // An operator may append a sheet the scanner missed entirely — such a
        // hand-written row has no confidence value.
        await ExportSingleAcceptedScan();
        MutateCell(row: 2, column: 5, value: "");

        var read = await sut.ReadAsync(workbookPath, CancellationToken.None);

        read.IsSuccess.Should().BeTrue(read.Error);
        read.Value!.GetScans().Single().Confidence.Should().Be(0);
    }

    [Fact]
    public async Task ReadAsync_Should_Fail_When_A_Row_Duplicates_Another_Sheet()
    {
        // A copy-paste slip in the review would otherwise count one sheet's
        // score twice in the standings.
        var original = new QuizResult();
        original.AddScan(new SheetScanResult(1, "R1/a.pdf", 12, 75, 0.91, ReviewStatus.Accepted, null));
        original.AddScan(new SheetScanResult(1, "R1/a.pdf", 12, 75, 0.91, ReviewStatus.Accepted, null));
        await exporter.ExportAsync(original, workbookPath, CancellationToken.None);

        var read = await sut.ReadAsync(workbookPath, CancellationToken.None);

        read.IsFailure.Should().BeTrue();
        read.Error.Should().Contain("Row 3").And.Contain("row 2").And.Contain("R1/a.pdf");
    }

    [Fact]
    public async Task ReadAsync_Should_Skip_A_Row_The_Operator_Cleared_Out()
    {
        // Clearing a row's cells is the same review action as deleting the
        // row (which leaves no trace), so the import treats them alike.
        var original = new QuizResult();
        original.AddScan(new SheetScanResult(1, "R1/a.pdf", 12, 75, 0.91, ReviewStatus.Accepted, null));
        original.AddScan(new SheetScanResult(2, "R2/b.pdf", 23, 100, 0.93, ReviewStatus.Accepted, null));
        await exporter.ExportAsync(original, workbookPath, CancellationToken.None);
        ClearRow(row: 2);

        var read = await sut.ReadAsync(workbookPath, CancellationToken.None);

        read.IsSuccess.Should().BeTrue(read.Error);
        read.Value!.GetScans().Single().SourcePath.Should().Be("R2/b.pdf");
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

    private void MutateNumericCell(int row, int column, double value)
    {
        using var workbook = new XLWorkbook(workbookPath);
        var sheet = workbook.Worksheet(ClosedXmlExcelExporter.ScansSheetName);
        sheet.Cell(row, column).Value = value;
        workbook.Save();
    }

    private void ClearRow(int row)
    {
        using var workbook = new XLWorkbook(workbookPath);
        var sheet = workbook.Worksheet(ClosedXmlExcelExporter.ScansSheetName);
        sheet.Row(row).Clear();
        workbook.Save();
    }
}
