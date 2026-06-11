namespace VisionCore.Application.Abstractions;

using VisionCore.Application.Common;
using VisionCore.Domain.Models;

/// <summary>
/// Reads a previously exported results workbook back into a
/// <see cref="QuizResult"/> after a human has reviewed it — corrected values
/// and promoted NeedsReview rows to Accepted (or demoted them to Rejected).
/// Implementations must validate the rows and fail with the offending row's
/// position rather than import a malformed review.
/// </summary>
public interface IReviewedScansReader
{
    Task<Result<QuizResult>> ReadAsync(string workbookPath, CancellationToken ct);
}
