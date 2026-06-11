using FluentAssertions;
using VisionCore.Domain.Imaging.Evaluation;
using Xunit;

namespace VisionCore.Tests.Integration.EndToEnd;

/// <summary>
/// End-to-end recognition over deliberately hard inputs, exercising the
/// production factory + pipeline:
///
///   * the classically confusable digit pairs (0/8, 1/7, 3/8, 5/6, 6/9, 2/7, 4/9)
///     in both team-id and score positions, including all-zero and all-nine
///     boundary values;
///   * scanner-style degradations (speckle noise, faint ink, JPEG artefacts,
///     off-centre print) at a level a production scan can realistically show.
///
/// Two guarantees are asserted: readable sheets are read correctly, and a
/// degraded sheet is never *silently* wrong — if recognition is not confident
/// it must surface as NeedsReview/Rejected rather than auto-accept a misread.
/// </summary>
public sealed class HardRecognitionEndToEndTests
{
    [Theory]
    [InlineData(0, 0)]     // "00" / "000" — nothing but zeros
    [InlineData(99, 999)]  // all nines (closed loops everywhere)
    [InlineData(80, 808)]  // 8 vs 0: same silhouette, different holes
    [InlineData(17, 171)]  // 1 vs 7: single stroke vs hooked stroke
    [InlineData(38, 383)]  // 3 vs 8: open vs closed loops
    [InlineData(56, 565)]  // 5 vs 6: lower loop confusion
    [InlineData(69, 696)]  // 6 vs 9: point symmetry
    [InlineData(27, 272)]  // 2 vs 7: diagonal confusion
    [InlineData(49, 494)]  // 4 vs 9: open vs closed top
    [InlineData(18, 181)]  // 1 vs 8: thinnest vs widest glyph
    public async Task Pipeline_Should_Read_Confusable_Digit_Pairs_Exactly(int teamId, int score)
    {
        var result = await ProcessSheetAsync(teamId, score, SheetDistortion.None);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.TeamId.Should().Be(teamId);
        result.Score.Should().Be(score);

        // A genuinely tight margin (e.g. 5 vs 6) may legitimately route to
        // review — what a confusable pair must never do is get dropped or
        // misread.
        result.ReviewStatus.Should().BeOneOf(ReviewStatus.Accepted, ReviewStatus.NeedsReview);
    }

    [Theory]
    [InlineData(400, 0, 100, 0, 0)]   // dusty scanner glass (dust beside the digits)
    [InlineData(0, 110, 100, 0, 0)]   // weak toner
    [InlineData(0, 0, 55, 0, 0)]      // heavy JPEG compression
    [InlineData(0, 0, 100, 3, 2)]     // off-centre print
    [InlineData(400, 80, 75, 3, 2)]   // a bit of everything
    public async Task Pipeline_Should_Read_A_Realistically_Degraded_Sheet(
        int speckles, int inkIntensity, int jpegQuality, int offsetX, int offsetY)
    {
        var distortion = new SheetDistortion
        {
            NoiseSpeckles = speckles,
            InkIntensity = (byte)inkIntensity,
            JpegQuality = jpegQuality,
            DigitOffsetX = offsetX,
            DigitOffsetY = offsetY,
            KeepSpecklesOffDigits = true
        };

        var result = await ProcessSheetAsync(teamId: 38, score: 583, distortion);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.TeamId.Should().Be(38);
        result.Score.Should().Be(583);
        result.ReviewStatus.Should().BeOneOf(ReviewStatus.Accepted, ReviewStatus.NeedsReview);
    }

    [Theory]
    [InlineData(400, 0, 100, 0, 0)]   // a speckle lands inside a digit cell and alters the glyph
    [InlineData(800, 0, 100, 0, 0)]   // enough dust that several speckles hit the boxes
    [InlineData(0, 0, 100, 5, 4)]     // print far enough off-centre that the glyph crosses the cell border
    [InlineData(3000, 130, 45, 8, 6)] // everything at once, well past realistic
    [InlineData(6000, 150, 35, 0, 0)] // blizzard of noise over faint ink
    public async Task Pipeline_Should_Never_Silently_Misread_A_Heavily_Degraded_Sheet(
        int speckles, int inkIntensity, int jpegQuality, int offsetX, int offsetY)
    {
        const int teamId = 38;
        const int score = 583;
        var distortion = new SheetDistortion
        {
            NoiseSpeckles = speckles,
            InkIntensity = (byte)inkIntensity,
            JpegQuality = jpegQuality,
            DigitOffsetX = offsetX,
            DigitOffsetY = offsetY
        };

        var result = await ProcessSheetAsync(teamId, score, distortion);

        // The pipeline may read it, flag it for review, or reject it — but an
        // auto-accepted wrong value is the one outcome that must never happen:
        // accepted results feed the standings without a human looking at them.
        if (result.IsSuccess && result.ReviewStatus == ReviewStatus.Accepted)
        {
            result.TeamId.Should().Be(teamId, "an auto-accepted team id must be correct");
            result.Score.Should().Be(score, "an auto-accepted score must be correct");
        }
    }

    private static async Task<VisionCore.Application.Imaging.PipelineResult> ProcessSheetAsync(
        int teamId, int score, SheetDistortion distortion)
    {
        var root = Path.Combine(Path.GetTempPath(), "vc-pdf-hard", Guid.NewGuid().ToString("N"));
        var pdfPath = Path.Combine(root, "R1", "sheet.pdf");

        var regions = SyntheticPdfDatasetBuilder.DefaultRegions();
        new SyntheticPdfDatasetBuilder().BuildSheet(pdfPath, teamId, score, distortion);

        var pipeline = PdfTestPipeline.CreateFactory(regions).CreateForSource(pdfPath);

        try
        {
            return await pipeline.ProcessAsync(pdfPath, CancellationToken.None);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
