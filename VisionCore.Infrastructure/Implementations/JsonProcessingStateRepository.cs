namespace VisionCore.Infrastructure.Implementations;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using VisionCore.Application.Abstractions;
using VisionCore.Domain.Entities;
using VisionCore.Domain.Imaging.Evaluation;

/// <summary>
/// Persists per-round processing state as a JSON file in the scan root
/// (<c>.visioncore-state.json</c>), so the state travels with the input data.
/// Honours the port's resilience contract: a missing, corrupt or
/// foreign-version file loads as empty state, and persistence failures are
/// logged but never thrown — the cache is an optimization, not a source of
/// truth. Domain results are mapped to explicit DTOs so the file schema does
/// not silently drift with the domain model.
/// </summary>
public sealed class JsonProcessingStateRepository(ILogger<JsonProcessingStateRepository> logger)
    : IProcessingStateRepository
{
    private const string StateFileName = ".visioncore-state.json";
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

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
                logger.LogWarning(
                    "Ignoring processing state at {Path}: unsupported document version", path);
                return new Dictionary<int, RoundProcessingState>();
            }

            return document.Rounds
                .Select(ToState)
                .OfType<RoundProcessingState>()
                .ToDictionary(state => state.Round);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "Ignoring unreadable processing state at {Path}", path);
            return new Dictionary<int, RoundProcessingState>();
        }
    }

    public async Task SaveAsync(string root, IReadOnlyCollection<RoundProcessingState> states, CancellationToken ct)
    {
        var path = StatePath(root);
        var document = new StateDocument(
            CurrentVersion,
            states.Select(ToDto).ToList());

        try
        {
            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, document, SerializerOptions, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not persist processing state to {Path}; the next run re-scans", path);
        }
    }

    private static string StatePath(string root) => Path.Combine(root, StateFileName);

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

    private sealed record StateDocument(int Version, List<RoundStateDto> Rounds);

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
