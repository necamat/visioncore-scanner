namespace VisionCore.Console;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VisionCore.Application.Abstractions;
using VisionCore.Application.Configuration;
using VisionCore.Application.UseCases;

/// <summary>
/// Console entry-point orchestrator: resolves the input/output paths, runs the
/// scan use case, then exports the result to Excel, and returns a process exit
/// code (0 on success, non-zero on failure).
/// </summary>
internal sealed class ConsoleOrchestrator(
    ScanQuizSheetsUseCase scanUseCase,
    IExcelExporter excelExporter,
    IOptions<ProcessingOptions> options,
    ILogger<ConsoleOrchestrator> logger)
{
    private const int ExitSuccess = 0;
    private const int ExitFailure = 1;

    private readonly ProcessingOptions _options = options.Value;

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        var inputFolder = args.Length > 0 ? args[0] : _options.InputFolder;
        var outputPath = Path.Combine(_options.OutputFolder, _options.OutputFileName);

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
}
