namespace VisionCore.Application.Mapping;

using VisionCore.Application.Imaging;
using VisionCore.Domain.Entities;
using VisionCore.Domain.Imaging.Evaluation;

/// <summary>
/// Maps a pipeline outcome (<see cref="PipelineResult"/>) onto the domain
/// <see cref="SheetScanResult"/> recorded for a round. Keeps the projection in
/// one place so the use-case stays focused on orchestration.
/// </summary>
public static class SheetScanResultMapper
{
    /// <summary>Maps a completed pipeline result for the given round and source file.</summary>
    public static SheetScanResult ToScanResult(this PipelineResult result, int round, string sourceFile) =>
        new(
            round,
            sourceFile,
            result.TeamId,
            result.Score,
            result.Confidence,
            result.ReviewStatus ?? ReviewStatus.Rejected,
            result.FailureCode);

    /// <summary>Builds a rejected scan for a source file that could not be processed at all.</summary>
    public static SheetScanResult Rejected(int round, string sourceFile) =>
        new(round, sourceFile, TeamId: null, Score: null, Confidence: 0, ReviewStatus.Rejected, FailureCode: null);
}
