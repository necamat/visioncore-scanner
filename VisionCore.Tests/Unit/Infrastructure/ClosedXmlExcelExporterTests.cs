using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VisionCore.Domain.Entities;
using VisionCore.Domain.Imaging.Evaluation;
using VisionCore.Domain.Models;
using VisionCore.Infrastructure.Implementations;
using Xunit;

namespace VisionCore.Tests.Unit.Infrastructure;

public sealed class ClosedXmlExcelExporterTests
{
    [Fact]
    public async Task ExportAsync_Should_Create_Scans_And_Standings_Worksheets()
    {
        // Arrange
        var sut = new ClosedXmlExcelExporter(NullLogger<ClosedXmlExcelExporter>.Instance);
        var quizResult = new QuizResult();
        quizResult.AddScan(new SheetScanResult(1, "r1\\img1.pdf", 7, 12, 0.95, ReviewStatus.Accepted, null));
        quizResult.AddScan(new SheetScanResult(1, "r1\\img2.pdf", 7, 8, 0.90, ReviewStatus.Accepted, null));
        quizResult.AddScan(new SheetScanResult(2, "r2\\img3.pdf", 9, null, 0.40, ReviewStatus.Rejected, EvaluationFailureCode.RecognitionFailed));

        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var outputPath = Path.Combine(root, "results", "quiz.xlsx");

        try
        {
            // Act
            var exportResult = await sut.ExportAsync(quizResult, outputPath, CancellationToken.None);

            // Assert
            exportResult.IsSuccess.Should().BeTrue();
            File.Exists(outputPath).Should().BeTrue();

            using var workbook = new XLWorkbook(outputPath);
            workbook.Worksheet(ClosedXmlExcelExporter.ScansSheetName).Cell(2, 1).GetValue<int>().Should().Be(1);
            workbook.Worksheet(ClosedXmlExcelExporter.ScansSheetName).Cell(2, 3).GetValue<int>().Should().Be(7);
            workbook.Worksheet(ClosedXmlExcelExporter.ScansSheetName).Cell(4, 6).GetValue<string>().Should().Be("Rejected");
            workbook.Worksheet(ClosedXmlExcelExporter.StandingsSheetName).Cell(2, 1).GetValue<int>().Should().Be(7);
            workbook.Worksheet(ClosedXmlExcelExporter.StandingsSheetName).Cell(2, 2).GetValue<int>().Should().Be(20);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task ExportAsync_Should_Overwrite_An_Existing_Workbook_Without_Leaving_A_Temp_File()
    {
        var sut = new ClosedXmlExcelExporter(NullLogger<ClosedXmlExcelExporter>.Instance);
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var outputPath = Path.Combine(root, "quiz.xlsx");

        var first = new QuizResult();
        first.AddScan(new SheetScanResult(1, "r1\\img1.pdf", 7, 12, 0.95, ReviewStatus.Accepted, null));
        var second = new QuizResult();
        second.AddScan(new SheetScanResult(1, "r1\\img1.pdf", 7, 99, 0.95, ReviewStatus.Accepted, null));

        try
        {
            (await sut.ExportAsync(first, outputPath, CancellationToken.None)).IsSuccess.Should().BeTrue();
            (await sut.ExportAsync(second, outputPath, CancellationToken.None)).IsSuccess.Should().BeTrue();

            using var workbook = new XLWorkbook(outputPath);
            workbook.Worksheet(ClosedXmlExcelExporter.ScansSheetName).Cell(2, 4).GetValue<int>().Should().Be(99);
            Directory.GetFiles(root).Should().ContainSingle(
                "the temp file of the atomic write must be gone after a successful export");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task ExportAsync_Should_Fail_Cleanly_And_Clean_Up_When_The_Target_Cannot_Be_Replaced()
    {
        // The atomic write must never destroy what sits at the target path —
        // a directory there makes the final move fail on every platform.
        var sut = new ClosedXmlExcelExporter(NullLogger<ClosedXmlExcelExporter>.Instance);
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var outputPath = Path.Combine(root, "quiz.xlsx");
        Directory.CreateDirectory(outputPath);

        try
        {
            var export = await sut.ExportAsync(new QuizResult(), outputPath, CancellationToken.None);

            export.IsFailure.Should().BeTrue();
            export.Error.Should().Contain("Excel export failed");
            Directory.Exists(outputPath).Should().BeTrue("the export must not clobber the target");
            Directory.GetFiles(root).Should().BeEmpty("the temp file must be cleaned up after a failure");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
