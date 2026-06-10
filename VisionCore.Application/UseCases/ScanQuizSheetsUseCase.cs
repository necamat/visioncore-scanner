namespace VisionCore.Application.UseCases;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VisionCore.Application.Abstractions;
using VisionCore.Application.Common;
using VisionCore.Application.Configuration;
using VisionCore.Application.Mapping;
using VisionCore.Domain.Entities;
using VisionCore.Domain.Models;

/// <summary>
/// Scans every source document supplied by the <see cref="IScanSourceProvider"/>
/// through the format-appropriate pipeline and collects the per-sheet results
/// into a <see cref="QuizResult"/>. Rounds whose files are unchanged since the
/// previous run reuse their persisted results (see
/// <see cref="IProcessingStateRepository"/>); the rest are scanned concurrently
/// (bounded by <see cref="ProcessingOptions.MaxDegreeOfParallelism"/>) while the
/// collected results keep the provider's order. Resilient per file — a single
/// sheet that fails is recorded as rejected and the run continues. Exporting
/// the result is the caller's responsibility.
/// </summary>
public sealed class ScanQuizSheetsUseCase(
    IScanSourceProvider sourceProvider,
    IPipelineFactory pipelineFactory,
    IProcessingStateRepository stateRepository,
    IOptions<ProcessingOptions> processingOptions,
    ILogger<ScanQuizSheetsUseCase> logger)
{
    public async Task<Result<QuizResult>> ScanAsync(string root, CancellationToken ct)
    {
        logger.LogInformation("Scanning sources under {Root}", root);

        if (!Directory.Exists(root))
        {
            logger.LogWarning("Root folder does not exist: {Root}", root);
            return Result<QuizResult>.Failure("Root folder does not exist");
        }

        IReadOnlyList<ScanSource> sources;
        try
        {
            sources = sourceProvider.GetSources(root).ToList();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Failed to enumerate scan sources under {Root}", root);
            return Result<QuizResult>.Failure(ex.Message);
        }

        var rounds = GroupByRoundPreservingOrder(sources);
        var previousState = await LoadPreviousStateAsync(root, ct);

        var roundStates = new List<RoundProcessingState>(rounds.Count);
        foreach (var (round, roundSources) in rounds)
        {
            ct.ThrowIfCancellationRequested();

            var fingerprints = Fingerprint(root, roundSources);
            if (previousState.TryGetValue(round, out var cached) &&
                cached.Sources.SequenceEqual(fingerprints))
            {
                logger.LogInformation(
                    "Round {Round} is unchanged — reusing {Count} persisted results", round, cached.Results.Count);
                roundStates.Add(cached);
                continue;
            }

            var results = await ScanRoundAsync(roundSources, ct);
            roundStates.Add(new RoundProcessingState(round, fingerprints, results));
        }

        await stateRepository.SaveAsync(root, roundStates, ct);

        var quizResult = new QuizResult();
        foreach (var scan in roundStates.SelectMany(state => state.Results))
        {
            quizResult.AddScan(scan);
        }

        return Result<QuizResult>.Success(quizResult);
    }

    /// <summary>Scans one round's sheets concurrently, keeping the provider's order.</summary>
    private async Task<IReadOnlyList<SheetScanResult>> ScanRoundAsync(
        IReadOnlyList<ScanSource> roundSources,
        CancellationToken ct)
    {
        var results = new SheetScanResult[roundSources.Count];
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = EffectiveDegreeOfParallelism,
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(
            Enumerable.Range(0, roundSources.Count),
            parallelOptions,
            async (index, token) => results[index] = await ScanSourceAsync(roundSources[index], token));

        return results;
    }

    private async Task<SheetScanResult> ScanSourceAsync(ScanSource source, CancellationToken ct)
    {
        logger.LogInformation("Processing {File}", source.SourcePath);

        try
        {
            var pipeline = pipelineFactory.CreateForSource(source.SourcePath);
            ct.ThrowIfCancellationRequested();
            var result = await pipeline.ProcessAsync(source.SourcePath, ct);

            var scan = result.ToScanResult(source.Round, source.SourcePath);
            logger.LogInformation("Processed result {@Result}", scan);
            return scan;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error while processing {File}", source.SourcePath);
            return SheetScanResultMapper.Rejected(source.Round, source.SourcePath);
        }
    }

    private async Task<IReadOnlyDictionary<int, RoundProcessingState>> LoadPreviousStateAsync(
        string root,
        CancellationToken ct)
    {
        if (!processingOptions.Value.ReuseUnchangedRounds)
        {
            return new Dictionary<int, RoundProcessingState>();
        }

        return await stateRepository.LoadAsync(root, ct);
    }

    private int EffectiveDegreeOfParallelism =>
        processingOptions.Value.MaxDegreeOfParallelism > 0
            ? processingOptions.Value.MaxDegreeOfParallelism
            : Environment.ProcessorCount;

    private static IReadOnlyList<(int Round, IReadOnlyList<ScanSource> Sources)> GroupByRoundPreservingOrder(
        IReadOnlyList<ScanSource> sources) =>
        sources
            .GroupBy(source => source.Round)
            .Select(group => (group.Key, (IReadOnlyList<ScanSource>)group.ToList()))
            .ToList();

    private static IReadOnlyList<SourceFingerprint> Fingerprint(
        string root,
        IReadOnlyList<ScanSource> roundSources) =>
        roundSources
            .Select(source =>
            {
                var info = new FileInfo(source.SourcePath);
                return new SourceFingerprint(
                    Path.GetRelativePath(root, source.SourcePath),
                    info.Exists ? info.Length : -1,
                    info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue);
            })
            .ToList();
}
