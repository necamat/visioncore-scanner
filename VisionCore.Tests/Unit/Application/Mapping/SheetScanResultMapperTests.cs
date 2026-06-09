using FluentAssertions;
using VisionCore.Application.Imaging;
using VisionCore.Application.Mapping;
using VisionCore.Domain.Imaging.Evaluation;
using Xunit;

namespace VisionCore.Tests.Unit.Application.Mapping;

public sealed class SheetScanResultMapperTests
{
    [Fact]
    public void ToScanResult_Should_Project_All_Fields()
    {
        var pipelineResult = new PipelineResult(
            IsSuccess: true,
            Error: null,
            TeamId: 12,
            Score: 75,
            Confidence: 0.91,
            RecognizedDigits: "12:075",
            TeamIdDigitConfidenceTrace: null,
            ScoreDigitConfidenceTrace: null,
            CroppedRegionsTrace: null,
            ReviewStatus: ReviewStatus.Accepted,
            FailureCode: null);

        var scan = pipelineResult.ToScanResult(round: 2, sourceFile: "R2/sheet.pdf");

        scan.Round.Should().Be(2);
        scan.SourcePath.Should().Be("R2/sheet.pdf");
        scan.TeamId.Should().Be(12);
        scan.Score.Should().Be(75);
        scan.Confidence.Should().Be(0.91);
        scan.Status.Should().Be(ReviewStatus.Accepted);
        scan.FailureCode.Should().BeNull();
    }

    [Fact]
    public void ToScanResult_Should_Default_To_Rejected_When_ReviewStatus_Missing()
    {
        var pipelineResult = new PipelineResult(
            IsSuccess: false, Error: "boom",
            TeamId: null, Score: null, Confidence: 0,
            RecognizedDigits: null, TeamIdDigitConfidenceTrace: null,
            ScoreDigitConfidenceTrace: null, CroppedRegionsTrace: null,
            ReviewStatus: null, FailureCode: null);

        var scan = pipelineResult.ToScanResult(round: 1, sourceFile: "R1/sheet.pdf");

        scan.Status.Should().Be(ReviewStatus.Rejected);
    }

    [Fact]
    public void Rejected_Should_Build_Empty_Rejected_Scan()
    {
        var scan = SheetScanResultMapper.Rejected(round: 3, sourceFile: "R3/broken.pdf");

        scan.Round.Should().Be(3);
        scan.SourcePath.Should().Be("R3/broken.pdf");
        scan.TeamId.Should().BeNull();
        scan.Score.Should().BeNull();
        scan.Status.Should().Be(ReviewStatus.Rejected);
    }
}
