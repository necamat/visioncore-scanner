namespace VisionCore.Console;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
                config.SetBasePath(Directory.GetCurrentDirectory());
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
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

        services.AddOptions<ConfidenceEvaluationOptions>()
            .Bind(context.Configuration.GetSection("ConfidenceEvaluationOptions"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<PdfRegionOptions>()
            .Bind(context.Configuration.GetSection("PdfRegions"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScanningInfrastructure();
        services.AddTransient<ScanQuizSheetsUseCase>();
        services.AddTransient<ConsoleOrchestrator>();
    }
}
