using VisionCore.Domain.Imaging.Evaluation;

namespace VisionCore.Tests.Integration.EndToEnd;

public sealed record RoundFolderDatasetManifest(
    IReadOnlyList<RoundFolderDatasetEntry> Entries);

public sealed record RoundFolderDatasetEntry(
    int Round,
    string FileName,
    int ExpectedTeamId,
    int ExpectedScore,
    ReviewStatus ExpectedStatus);
