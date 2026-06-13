namespace VisionCore.Console;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using VisionCore.Application.Configuration;
using VisionCore.Application.UseCases;
using VisionCore.Infrastructure;

/// <summary>
/// Constructs and configures the application host.
/// Separates infrastructure wiring from the program entry point.
/// </summary>
internal static class AppHost
{
    public static IHost Build(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((_, config) =>
            {
                // Read appsettings.json relative to the working directory so a
                // published binary picks up the config next to where it is run.
                // Re-apply environment variables and command-line args *after*
                // the file, otherwise this re-added JSON source would shadow the
                // ones CreateDefaultBuilder registered and standard overrides
                // (e.g. DigitRecognitionOptions__ScoreEngine=Onnx) would be
                // silently ignored.
                config.SetBasePath(Directory.GetCurrentDirectory());
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                config.AddEnvironmentVariables();
                config.AddCommandLine(args);
            })
            .UseSerilog((context, loggerConfiguration) =>
                loggerConfiguration.ReadFrom.Configuration(context.Configuration))
            .ConfigureServices(ConfigureServices)
            .Build();

    private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        services.AddOptions<ProcessingOptions>()
            .Bind(context.Configuration.GetSection("ProcessingOptions"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ScanSourceOptions>()
            .Bind(context.Configuration.GetSection("ScanSource"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<DigitRecognitionOptions>()
            .Bind(context.Configuration.GetSection("DigitRecognitionOptions"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The cross-field rules (threshold ordering, heuristic-confidence
        // contract) live in the dedicated validator.
        services.AddSingleton<IValidateOptions<ConfidenceEvaluationOptions>, ConfidenceEvaluationOptionsValidator>();
        services.AddOptions<ConfidenceEvaluationOptions>()
            .Bind(context.Configuration.GetSection("ConfidenceEvaluationOptions"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // PdfRegionBounds is nested, which DataAnnotations validation does not
        // reach — the dedicated validator checks the rectangles themselves.
        services.AddSingleton<IValidateOptions<PdfRegionOptions>, PdfRegionOptionsValidator>();
        services.AddOptions<PdfRegionOptions>()
            .Bind(context.Configuration.GetSection("PdfRegions"))
            .ValidateOnStart();

        services.AddScanningInfrastructure();
        services.AddTransient<ScanQuizSheetsUseCase>();
        services.AddTransient<FinalizeQuizResultUseCase>();
        services.AddTransient<ConsoleOrchestrator>();
    }
}
