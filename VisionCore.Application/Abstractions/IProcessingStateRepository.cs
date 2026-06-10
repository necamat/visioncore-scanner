namespace VisionCore.Application.Abstractions;

using VisionCore.Domain.Entities;

/// <summary>
/// Identifies one source file at a point in time: its path relative to the
/// scan root plus size and timestamp. Two runs see the same fingerprint only
/// if the file has not changed in between.
/// </summary>
public sealed record SourceFingerprint(string RelativePath, long FileSizeBytes, DateTime LastWriteUtc);

/// <summary>
/// The outcome of scanning one round: the fingerprints of the files that were
/// read and the per-sheet results they produced. A later run may reuse the
/// results verbatim when every fingerprint still matches.
/// </summary>
public sealed record RoundProcessingState(
    int Round,
    IReadOnlyList<SourceFingerprint> Sources,
    IReadOnlyList<SheetScanResult> Results);

/// <summary>
/// Persists per-round processing state between runs so unchanged rounds are
/// not scanned again. Implementations must be resilient: a missing or
/// unreadable state store yields an empty state, and a failure to persist must
/// be logged but never fail the run — the cache is an optimization, not a
/// source of truth.
/// </summary>
public interface IProcessingStateRepository
{
    /// <summary>Loads the persisted state for the given scan root, keyed by round.</summary>
    Task<IReadOnlyDictionary<int, RoundProcessingState>> LoadAsync(string root, CancellationToken ct);

    /// <summary>Persists the state of every round of the current run for the given scan root.</summary>
    Task SaveAsync(string root, IReadOnlyCollection<RoundProcessingState> states, CancellationToken ct);
}
