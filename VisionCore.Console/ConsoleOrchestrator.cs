namespace VisionCore.Console;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VisionCore.Application.Abstractions;
using VisionCore.Application.Configuration;
using VisionCore.Application.UseCases;

/// <summary>
/// Console entry-point orchestrator: resolves the input/output paths, runs the
/// scan use case, then exports the result to Excel, and returns a process exit
/// code (0 on success, non-zero on failure). With <c>--finalize</c> it instead
/// re-imports a human-reviewed workbook and regenerates its standings.
/// </summary>
internal sealed class ConsoleOrchestrator(
    ScanQuizSheetsUseCase scanUseCase,
    FinalizeQuizResultUseCase finalizeUseCase,
    IExcelExporter excelExporter,
    IOptions<ProcessingOptions> options,
    ILogger<ConsoleOrchestrator> logger)
{
    /// <summary>Re-imports a reviewed workbook instead of scanning: <c>--finalize [workbook]</c>.</summary>
    internal const string FinalizeFlag = "--finalize";

    private const int ExitSuccess = 0;
    private const int ExitFailure = 1;

    private readonly ProcessingOptions _options = options.Value;

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        var outputPath = Path.Combine(_options.OutputFolder, _options.OutputFileName);

        if (args.Length > 0 && args[0] == FinalizeFlag)
        {
            var workbookPath = args.Length > 1 ? args[1] : outputPath;
            return await FinalizeAsync(workbookPath, ct);
        }

        return await ScanAsync(args, outputPath, ct);
    }

    private async Task<int> ScanAsync(string[] args, string outputPath, CancellationToken ct)
    {
        var inputFolder = args.Length > 0 ? args[0] : _options.InputFolder;

        logger.LogInformation("Input  : {Input}", inputFolder);

        var scan = await scanUseCase.ScanAsync(inputFolder, ct);
        if (scan.IsFailure)
        {
            logger.LogError("Scan failed: {Error}", scan.Error);
            return ExitFailure;
        }

        var export = await excelExporter.ExportAsync(scan.Value!, outputPath, ct);
        if (export.IsFailure)
        {
            logger.LogError("Export failed: {Error}", export.Error);
            return ExitFailure;
        }

        logger.LogInformation("Done. Output: {Output}", outputPath);
        return ExitSuccess;
    }

    private async Task<int> FinalizeAsync(string workbookPath, CancellationToken ct)
    {
        var finalize = await finalizeUseCase.FinalizeAsync(workbookPath, ct);
        if (finalize.IsFailure)
        {
            logger.LogError("Finalize failed: {Error}", finalize.Error);
            return ExitFailure;
        }

        logger.LogInformation("Done. Finalized: {Workbook}", workbookPath);
        return ExitSuccess;
    }
}
