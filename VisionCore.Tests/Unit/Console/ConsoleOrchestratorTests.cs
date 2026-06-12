using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using VisionCore.Application.Abstractions;
using VisionCore.Application.Common;
using VisionCore.Application.Configuration;
using VisionCore.Application.UseCases;
using VisionCore.Console;
using VisionCore.Domain.Models;
using Xunit;

namespace VisionCore.Tests.Unit.Console;

/// <summary>
/// Covers the orchestrator's command-line routing: a plain run scans and
/// exports, <c>--finalize</c> re-imports a reviewed workbook (explicit path or
/// the configured output), and every failure maps to a non-zero exit code.
/// The use cases are real instances over mocked ports, so the tests exercise
/// the exact wiring the console host runs.
/// </summary>
public sealed class ConsoleOrchestratorTests : IDisposable
{
    private readonly Mock<IScanSourceProvider> sourceProviderMock = new();
    private readonly Mock<IPipelineFactory> pipelineFactoryMock = new();
    private readonly Mock<IProcessingStateRepository> stateRepositoryMock = new();
    private readonly Mock<IReviewedScansReader> reviewedScansReaderMock = new();
    private readonly Mock<IExcelExporter> exporterMock = new();

    private readonly ProcessingOptions options = new();
    private readonly string inputRoot;
    private readonly ConsoleOrchestrator sut;

    public ConsoleOrchestratorTests()
    {
        inputRoot = Path.Combine(Path.GetTempPath(), "vc-orchestrator", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(inputRoot);

        sourceProviderMock
            .Setup(p => p.GetSources(It.IsAny<string>()))
            .Returns([]);
        stateRepositoryMock
            .Setup(r => r.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, RoundProcessingState>());
        exporterMock
            .Setup(e => e.ExportAsync(It.IsAny<QuizResult>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var scanUseCase = new ScanQuizSheetsUseCase(
            sourceProviderMock.Object,
            pipelineFactoryMock.Object,
            stateRepositoryMock.Object,
            Options.Create(options),
            NullLogger<ScanQuizSheetsUseCase>.Instance);
        var finalizeUseCase = new FinalizeQuizResultUseCase(
            reviewedScansReaderMock.Object,
            exporterMock.Object,
            NullLogger<FinalizeQuizResultUseCase>.Instance);

        sut = new ConsoleOrchestrator(
            scanUseCase,
            finalizeUseCase,
            exporterMock.Object,
            Options.Create(options),
            NullLogger<ConsoleOrchestrator>.Instance);
    }

    public void Dispose() => Directory.Delete(inputRoot, true);

    private string ConfiguredOutputPath => Path.Combine(options.OutputFolder, options.OutputFileName);

    [Fact]
    public async Task RunAsync_Should_Scan_And_Export_To_The_Configured_Output_Path()
    {
        var exitCode = await sut.RunAsync([inputRoot], CancellationToken.None);

        exitCode.Should().Be(0);
        sourceProviderMock.Verify(p => p.GetSources(inputRoot), Times.Once);
        exporterMock.Verify(
            e => e.ExportAsync(It.IsAny<QuizResult>(), ConfiguredOutputPath, It.IsAny<CancellationToken>()),
            Times.Once);
        reviewedScansReaderMock.Verify(
            r => r.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_Should_Route_The_Finalize_Flag_To_The_Given_Workbook()
    {
        var workbookPath = Path.Combine(inputRoot, "reviewed.xlsx");
        reviewedScansReaderMock
            .Setup(r => r.ReadAsync(workbookPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<QuizResult>.Success(new QuizResult()));

        var exitCode = await sut.RunAsync(
            [ConsoleOrchestrator.FinalizeFlag, workbookPath], CancellationToken.None);

        exitCode.Should().Be(0);
        reviewedScansReaderMock.Verify(
            r => r.ReadAsync(workbookPath, It.IsAny<CancellationToken>()), Times.Once);
        exporterMock.Verify(
            e => e.ExportAsync(It.IsAny<QuizResult>(), workbookPath, It.IsAny<CancellationToken>()),
            Times.Once);
        sourceProviderMock.Verify(p => p.GetSources(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_Should_Default_The_Finalize_Workbook_To_The_Configured_Output()
    {
        reviewedScansReaderMock
            .Setup(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<QuizResult>.Success(new QuizResult()));

        var exitCode = await sut.RunAsync([ConsoleOrchestrator.FinalizeFlag], CancellationToken.None);

        exitCode.Should().Be(0);
        reviewedScansReaderMock.Verify(
            r => r.ReadAsync(ConfiguredOutputPath, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_Should_Return_A_Failure_Exit_Code_When_The_Finalize_Import_Fails()
    {
        reviewedScansReaderMock
            .Setup(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<QuizResult>.Failure("Row 2: Status 'Aproved' is not one of ..."));

        var exitCode = await sut.RunAsync([ConsoleOrchestrator.FinalizeFlag], CancellationToken.None);

        exitCode.Should().Be(1);
        exporterMock.Verify(
            e => e.ExportAsync(It.IsAny<QuizResult>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_Should_Return_A_Failure_Exit_Code_When_The_Scan_Fails()
    {
        var missingRoot = Path.Combine(inputRoot, "does-not-exist");

        var exitCode = await sut.RunAsync([missingRoot], CancellationToken.None);

        exitCode.Should().Be(1);
        exporterMock.Verify(
            e => e.ExportAsync(It.IsAny<QuizResult>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
