namespace VisionCore.Infrastructure.Implementations;

using Microsoft.Extensions.Options;
using VisionCore.Application.Abstractions;
using VisionCore.Application.Configuration;

/// <summary>
/// Discovers scan sources from round subfolders on disk: each folder named with
/// the configured prefix plus a number (e.g. <c>R1</c>, <c>R2</c>) contributes
/// its matching files for that round, ordered deterministically.
/// </summary>
public sealed class RoundFolderScanSourceProvider(IOptions<ScanSourceOptions> options) : IScanSourceProvider
{
    private static readonly string[] DefaultPatterns = ["*.pdf"];

    private readonly ScanSourceOptions _options = options.Value;

    /// <inheritdoc />
    public IEnumerable<ScanSource> GetSources(string root)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        // Materialize eagerly so any file-system error surfaces here (and is
        // wrapped), not lazily mid-iteration inside the use case.
        var patterns = _options.SearchPatterns.Length > 0 ? _options.SearchPatterns : DefaultPatterns;
        var sources = new List<ScanSource>();

        try
        {
            foreach (var roundFolder in Directory.GetDirectories(root).OrderBy(f => f, StringComparer.Ordinal))
            {
                if (!TryParseRound(Path.GetFileName(roundFolder), out var round))
                {
                    continue;
                }

                var files = patterns
                    .SelectMany(pattern => Directory.GetFiles(roundFolder, pattern))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, StringComparer.Ordinal);

                sources.AddRange(files.Select(file => new ScanSource(round, file)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to enumerate scan sources under '{root}'.", ex);
        }

        return sources;
    }

    private bool TryParseRound(string folderName, out int round)
    {
        round = 0;
        return folderName.StartsWith(_options.RoundFolderPrefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(folderName.AsSpan(_options.RoundFolderPrefix.Length), out round);
    }
}
