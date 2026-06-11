namespace VisionCore.Infrastructure.Implementations;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VisionCore.Application.Abstractions;
using VisionCore.Application.Configuration;
using VisionCore.Domain.Entities;
using VisionCore.Domain.Imaging.Evaluation;

/// <summary>
/// Persists per-round processing state as a JSON file in the scan root
/// (<c>.visioncore-state.json</c>), so the state travels with the input data.
/// Honours the port's resilience contract: a missing, corrupt or
/// foreign-version file loads as empty state, malformed rounds are skipped
/// with a warning, and persistence failures are logged but never thrown — the
/// cache is an optimization, not a source of truth. The file carries a
/// fingerprint of the recognition-relevant configuration, so changing
/// thresholds, template tunables or crop regions invalidates the cache
/// instead of replaying results produced under different rules. Saves are
/// atomic (temp file + move) so an interrupted run cannot leave a truncated
/// state behind. Domain results are mapped to explicit DTOs so the file
/// schema does not silently drift with the domain model.
/// </summary>
public sealed class JsonProcessingStateRepository : IProcessingStateRepository
{
    private const string StateFileName = ".visioncore-state.json";
    private const int CurrentVersion = 2;

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly ILogger<JsonProcessingStateRepository> _logger;
    private readonly string _configurationFingerprint;

    public JsonProcessingStateRepository(
        IOptions<DigitRecognitionOptions> digitOptions,
        IOptions<ConfidenceEvaluationOptions> confidenceOptions,
        IOptions<PdfRegionOptions> regionOptions,
        ILogger<JsonProcessingStateRepository> logger)
    {
        _logger = logger;
        _configurationFingerprint = ComputeConfigurationFingerprint(
            digitOptions.Value, confidenceOptions.Value, regionOptions.Value);
    }

    public async Task<IReadOnlyDictionary<int, RoundProcessingState>> LoadAsync(string root, CancellationToken ct)
    {
        var path = StatePath(root);
        if (!File.Exists(path))
        {
            return new Dictionary<int, RoundProcessingState>();
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var document = await JsonSerializer.DeserializeAsync<StateDocument>(stream, SerializerOptions, ct);

            if (document is null || document.Version != CurrentVersion)
            {
                _logger.LogWarning(
                    "Ignoring processing state at {Path}: unsupported document version", path);
                return new Dictionary<int, RoundProcessingState>();
            }

            if (document.ConfigurationFingerprint != _configurationFingerprint)
            {
                _logger.LogInformation(
                    "Ignoring processing state at {Path}: the recognition configuration has changed " +
                    "since it was written, so every round is re-scanned", path);
                return new Dictionary<int, RoundProcessingState>();
            }

            var states = new Dictionary<int, RoundProcessingState>();
            foreach (var roundDto in document.Rounds)
            {
                var state = ToState(roundDto);
                if (state is null)
                {
                    _logger.LogWarning(
                        "Ignoring round {Round} in {Path}: it carries an unknown status or failure code",
                        roundDto.Round, path);
                    continue;
                }

                if (!states.TryAdd(state.Round, state))
                {
                    _logger.LogWarning(
                        "Ignoring duplicate round {Round} entry in {Path}; keeping the first occurrence",
                        state.Round, path);
                }
            }

            return states;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Ignoring unreadable processing state at {Path}", path);
            return new Dictionary<int, RoundProcessingState>();
        }
    }

    public async Task SaveAsync(string root, IReadOnlyCollection<RoundProcessingState> states, CancellationToken ct)
    {
        var path = StatePath(root);
        var document = new StateDocument(
            CurrentVersion,
            _configurationFingerprint,
            states.Select(ToDto).ToList());

        // Write to a sibling temp file and move it into place, so a crash or
        // cancellation mid-write can never leave a truncated state file.
        var tempPath = path + ".tmp";
        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, document, SerializerOptions, ct);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not persist processing state to {Path}; the next run re-scans", path);
        }
    }

    private static string StatePath(string root) => Path.Combine(root, StateFileName);

    /// <summary>
    /// Hash of every option set that influences recognition results. Stored
    /// in the state file so results produced under a different configuration
    /// are never reused.
    /// </summary>
    private static string ComputeConfigurationFingerprint(
        DigitRecognitionOptions digitOptions,
        ConfidenceEvaluationOptions confidenceOptions,
        PdfRegionOptions regionOptions)
    {
        var json = JsonSerializer.Serialize(new
        {
            Digit = digitOptions,
            Confidence = confidenceOptions,
            Regions = regionOptions
        });

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static RoundProcessingState? ToState(RoundStateDto dto)
    {
        var results = new List<SheetScanResult>(dto.Results.Count);
        foreach (var result in dto.Results)
        {
            if (!Enum.TryParse<ReviewStatus>(result.Status, out var status))
            {
                // An unknown status means the file came from a newer schema —
                // drop the whole round rather than guess.
                return null;
            }

            EvaluationFailureCode? failureCode = null;
            if (result.FailureCode is not null)
            {
                if (!Enum.TryParse<EvaluationFailureCode>(result.FailureCode, out var parsed))
                {
                    return null;
                }

                failureCode = parsed;
            }

            results.Add(new SheetScanResult(
                result.Round, result.SourcePath, result.TeamId, result.Score,
                result.Confidence, status, failureCode));
        }

        var sources = dto.Sources
            .Select(source => new SourceFingerprint(source.RelativePath, source.FileSizeBytes, source.LastWriteUtc))
            .ToList();

        return new RoundProcessingState(dto.Round, sources, results);
    }

    private static RoundStateDto ToDto(RoundProcessingState state) =>
        new(
            state.Round,
            state.Sources
                .Select(source => new FingerprintDto(source.RelativePath, source.FileSizeBytes, source.LastWriteUtc))
                .ToList(),
            state.Results
                .Select(result => new SheetResultDto(
                    result.Round, result.SourcePath, result.TeamId, result.Score,
                    result.Confidence, result.Status.ToString(), result.FailureCode?.ToString()))
                .ToList());

    private sealed record StateDocument(int Version, string ConfigurationFingerprint, List<RoundStateDto> Rounds);

    private sealed record RoundStateDto(int Round, List<FingerprintDto> Sources, List<SheetResultDto> Results);

    private sealed record FingerprintDto(string RelativePath, long FileSizeBytes, DateTime LastWriteUtc);

    private sealed record SheetResultDto(
        int Round,
        string SourcePath,
        int? TeamId,
        int? Score,
        double Confidence,
        string Status,
        string? FailureCode);
}
