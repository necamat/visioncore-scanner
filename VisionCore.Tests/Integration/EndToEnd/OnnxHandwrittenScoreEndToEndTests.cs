using FluentAssertions;
using VisionCore.Domain.Imaging.Evaluation;
using Xunit;

namespace VisionCore.Tests.Integration.EndToEnd;

/// <summary>
/// End-to-end proof that the ONNX score engine reads genuinely handwritten
/// digits through the real PDF path: a sheet is built with real MNIST samples
/// pasted into the three score cells (the team id stays printed), embedded in a
/// PDF, then run through the production factory + pipeline with the score engine
/// set to ONNX. The team id is read by template matching, the score by the MNIST
/// CNN, and both must come out exactly right.
///
/// Scope: this drives real handwritten digits through real region extraction,
/// glyph isolation and recognition — a step beyond the unit tests, which feed
/// the classifier synthetic cells. It is still not a photo of a pen-filled,
/// scanned form (no paper texture, skew or pen variation); the MNIST sample is
/// placed cleanly inside the cell. The sample/score combinations below are
/// fixed so the run is deterministic across platforms.
/// </summary>
public sealed class OnnxHandwrittenScoreEndToEndTests
{
    [Theory]
    [InlineData(12, "5_0 8_0 3_0", 583)]
    [InlineData(7, "1_1 9_0 6_0", 196)]
    [InlineData(34, "7_0 0_1 5_1", 705)]
    [InlineData(23, "0_2 4_1 2_0", 42)]
    public async Task Pipeline_Should_Read_A_Handwritten_Score_With_The_Onnx_Engine(
        int teamId, string scoreSamples, int expectedScore)
    {
        var root = Path.Combine(Path.GetTempPath(), "vc-onnx-hw-e2e", Guid.NewGuid().ToString("N"));
        var pdfPath = Path.Combine(root, "R1", "sheet.pdf");
        var regions = SyntheticPdfDatasetBuilder.DefaultRegions();

        var scorePngs = scoreSamples
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(name => File.ReadAllBytes(Path.Combine("TestData", "Mnist", $"{name}.png")))
            .ToArray();
        new SyntheticPdfDatasetBuilder().BuildSheetWithHandwrittenScore(pdfPath, teamId, scorePngs);

        var pipeline = PdfTestPipeline.CreateOnnxFactory(regions).CreateForSource(pdfPath);

        try
        {
            var result = await pipeline.ProcessAsync(pdfPath, CancellationToken.None);

            result.IsSuccess.Should().BeTrue(result.Error);
            result.TeamId.Should().Be(teamId, "the printed team id is read by template matching");
            result.Score.Should().Be(expectedScore, "the handwritten score is read by the ONNX engine");

            // A correct read may legitimately land in the review band; the one
            // outcome that must never happen is a wrong auto-accept.
            result.ReviewStatus.Should().BeOneOf(ReviewStatus.Accepted, ReviewStatus.NeedsReview);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("0_0 0_1 0_2", 0)]    // all zeros
    [InlineData("9_0 9_1 9_2", 999)]  // all nines
    [InlineData("1_0 7_0 1_1", 171)]  // 1 vs 7
    [InlineData("6_0 9_0 6_1", 696)]  // 6 vs 9
    [InlineData("3_0 8_0 3_1", 383)]  // 3 vs 8
    [InlineData("5_0 6_0 5_1", 565)]  // 5 vs 6
    [InlineData("2_0 7_1 2_1", 272)]  // 2 vs 7
    [InlineData("4_0 9_2 4_1", 494)]  // 4 vs 9
    public async Task Pipeline_Should_Never_Silently_Misread_A_Hard_Handwritten_Score(
        string scoreSamples, int expectedScore)
    {
        var root = Path.Combine(Path.GetTempPath(), "vc-onnx-hard-e2e", Guid.NewGuid().ToString("N"));
        var pdfPath = Path.Combine(root, "R1", "sheet.pdf");
        var regions = SyntheticPdfDatasetBuilder.DefaultRegions();

        var scorePngs = scoreSamples
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(name => File.ReadAllBytes(Path.Combine("TestData", "Mnist", $"{name}.png")))
            .ToArray();
        new SyntheticPdfDatasetBuilder().BuildSheetWithHandwrittenScore(pdfPath, teamId: 12, scorePngs);

        var pipeline = PdfTestPipeline.CreateOnnxFactory(regions).CreateForSource(pdfPath);

        try
        {
            var result = await pipeline.ProcessAsync(pdfPath, CancellationToken.None);

            // Same guarantee as the template hard suite: an ambiguous handwritten
            // digit may route to review or be rejected, but an auto-accepted score
            // must never be wrong — accepted results feed the standings unseen.
            if (result.IsSuccess)
            {
                result.ReviewStatus.Should().NotBeNull("a successful run must carry a review decision");
                if (result.ReviewStatus == ReviewStatus.Accepted)
                {
                    result.Score.Should().Be(expectedScore, "an auto-accepted handwritten score must be correct");
                }
            }
            else
            {
                (result.Error ?? result.FailureCode?.ToString())
                    .Should().NotBeNullOrEmpty("a failed run must say why it failed");
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
