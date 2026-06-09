using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VisionCore.Application.Configuration;
using VisionCore.Application.Imaging;
using VisionCore.Application.UseCases;
using VisionCore.Domain.Imaging.Evaluation;
using VisionCore.Infrastructure.Implementations;
using VisionCore.Infrastructure.Implementations.Pdf;
using Xunit;

namespace VisionCore.Tests.Integration.EndToEnd;

/// <summary>
/// End-to-end coverage of the PDF source path:
/// synthetic scanned PDF -> PipelineFactory (.pdf branch) -> PdfRegionExtractor
/// -> TemplateMatching digit recognition -> confidence evaluation -> Excel export.
///
/// Uses the production <see cref="PipelineFactory"/> so file-extension routing
/// and PDF pipeline assembly are exercised exactly as in the console host.
/// </summary>
public sealed class PdfScanEndToEndTests
{
    [Fact]
    public async Task ExecuteAsync_Should_Process_Pdf_Round_Folders_And_Generate_Excel_Output()
    {
        // Arrange
        var root = NewTempRoot();
        var outputPath = Path.Combine(root, "output", "results.xlsx");

        var manifest = new RoundFolderDatasetManifest(
        [
            new RoundFolderDatasetEntry(1, "sheet-001.pdf", 12, 75, ReviewStatus.Accepted),
            new RoundFolderDatasetEntry(2, "sheet-002.pdf", 23, 100, ReviewStatus.Accepted)
        ]);

        var regions = SyntheticPdfDatasetBuilder.DefaultRegions();
        new SyntheticPdfDatasetBuilder().Build(root, manifest);

        var scanUseCase = PdfTestPipeline.CreateScanUseCase(regions);
        var exporter = new ClosedXmlExcelExporter(NullLogger<ClosedXmlExcelExporter>.Instance);

        try
        {
            // Act
            var scan = await scanUseCase.ScanAsync(root, CancellationToken.None);
            scan.IsSuccess.Should().BeTrue();
            var export = await exporter.ExportAsync(scan.Value!, outputPath, CancellationToken.None);

            // Assert
            export.IsSuccess.Should().BeTrue();
            File.Exists(outputPath).Should().BeTrue();

            using var workbook = new XLWorkbook(outputPath);
            var scans = workbook.Worksheet(ClosedXmlExcelExporter.ScansSheetName);

            scans.Cell(2, 1).GetValue<int>().Should().Be(1);
            scans.Cell(2, 3).GetValue<int>().Should().Be(12);
            scans.Cell(2, 4).GetValue<int>().Should().Be(75);
            scans.Cell(2, 6).GetValue<string>().Should().Be(ReviewStatus.Accepted.ToString());

            scans.Cell(3, 1).GetValue<int>().Should().Be(2);
            scans.Cell(3, 3).GetValue<int>().Should().Be(23);
            scans.Cell(3, 4).GetValue<int>().Should().Be(100);
            scans.Cell(3, 6).GetValue<string>().Should().Be(ReviewStatus.Accepted.ToString());

            var standings = workbook.Worksheet(ClosedXmlExcelExporter.StandingsSheetName);
            standings.Cell(2, 2).GetValue<int>().Should().Be(100);
            standings.Cell(3, 2).GetValue<int>().Should().Be(75);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(10, 234)] // digits 1,0,2,3,4
    [InlineData(56, 789)] // digits 5,6,7,8,9
    [InlineData(90, 1)]   // digits 9,0,0,0,1
    public async Task Pipeline_Should_Recognise_Every_Digit_Value(int teamId, int score)
    {
        // Arrange
        var root = NewTempRoot();
        var pdfPath = Path.Combine(root, "R1", "sheet.pdf");

        var regions = SyntheticPdfDatasetBuilder.DefaultRegions();
        new SyntheticPdfDatasetBuilder().BuildSheet(pdfPath, teamId, score);

        var pipeline = PdfTestPipeline.CreateFactory(regions).CreateForSource(pdfPath);

        try
        {
            // Act
            var result = await pipeline.ProcessAsync(pdfPath, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue(result.Error);
            result.TeamId.Should().Be(teamId);
            result.Score.Should().Be(score);
            result.ReviewStatus.Should().Be(ReviewStatus.Accepted);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Should_Reject_Sheet_With_Blank_Score()
    {
        // Arrange
        var root = NewTempRoot();
        var outputPath = Path.Combine(root, "output", "results.xlsx");

        var regions = SyntheticPdfDatasetBuilder.DefaultRegions();
        new SyntheticPdfDatasetBuilder().BuildSheet(Path.Combine(root, "R1", "sheet.pdf"), teamId: 42, score: null);

        var scanUseCase = PdfTestPipeline.CreateScanUseCase(regions);
        var exporter = new ClosedXmlExcelExporter(NullLogger<ClosedXmlExcelExporter>.Instance);

        try
        {
            // Act
            var scan = await scanUseCase.ScanAsync(root, CancellationToken.None);
            scan.IsSuccess.Should().BeTrue();
            var export = await exporter.ExportAsync(scan.Value!, outputPath, CancellationToken.None);

            // Assert: the run still completes and produces a report; the sheet is rejected.
            export.IsSuccess.Should().BeTrue();

            using var workbook = new XLWorkbook(outputPath);
            var scans = workbook.Worksheet(ClosedXmlExcelExporter.ScansSheetName);
            scans.Cell(2, 6).GetValue<string>().Should().Be(ReviewStatus.Rejected.ToString());
            scans.Cell(2, 4).IsEmpty().Should().BeTrue("a rejected sheet has no recognised score");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void PipelineFactory_Should_Route_By_File_Extension()
    {
        var factory = PdfTestPipeline.CreateFactory(SyntheticPdfDatasetBuilder.DefaultRegions());

        factory.CreateForSource("sheet.pdf").Should().BeOfType<StepPipeline>();
        factory.Invoking(f => f.CreateForSource("sheet.png"))
            .Should().Throw<NotSupportedException>();
    }

    private static string NewTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "vc-pdf-e2e", Guid.NewGuid().ToString("N"));
    }
}
