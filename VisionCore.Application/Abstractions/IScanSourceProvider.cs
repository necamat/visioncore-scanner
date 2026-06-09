namespace VisionCore.Application.Abstractions;

/// <summary>
/// A single source document to scan: its round number and a path the pipeline
/// can open.
/// </summary>
public sealed record ScanSource(int Round, string SourcePath);

/// <summary>
/// Supplies the set of source documents for a run. Abstracts <em>where</em> the
/// sources come from (round subfolders today; other origins — e.g. an external
/// import — can plug in behind this port) from the use case that processes them.
/// </summary>
public interface IScanSourceProvider
{
    /// <summary>
    /// Enumerates the source documents to process from the given root location.
    /// </summary>
    IEnumerable<ScanSource> GetSources(string root);
}
