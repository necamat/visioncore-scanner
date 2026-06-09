namespace VisionCore.Application.Configuration;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Settings for discovering source documents to scan. Bound from the
/// "ScanSource" configuration section.
/// </summary>
public sealed class ScanSourceOptions
{
    /// <summary>Prefix that marks a round subfolder (e.g. "R" → R1, R2, ...).</summary>
    [Required]
    public string RoundFolderPrefix { get; init; } = "R";

    /// <summary>
    /// File search patterns for source documents within a round folder,
    /// e.g. <c>["*.pdf"]</c>. Lets new source formats be enabled via config.
    /// Left empty by default: a pre-populated array would be *merged* with the
    /// configuration values (config binding appends by index), producing
    /// duplicates. The provider falls back to <c>*.pdf</c> when none are set.
    /// </summary>
    public string[] SearchPatterns { get; init; } = [];
}
