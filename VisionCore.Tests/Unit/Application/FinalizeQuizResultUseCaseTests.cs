using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using VisionCore.Application.Abstractions;
using VisionCore.Application.Common;
using VisionCore.Application.UseCases;
using VisionCore.Domain.Entities;
using VisionCore.Domain.Imaging.Evaluation;
using VisionCore.Domain.Models;
using Xunit;

namespace VisionCore.Tests.Unit.Application;

public sealed class FinalizeQuizResultUseCaseTests
{
    private const string WorkbookPath = "output/results.xlsx";

    private readonly Mock<IReviewedScansReader> readerMock = new();
    private readonly Mock<IExcelExporter> exporterMock = new();
    private readonly FinalizeQuizResultUseCase sut;

    public FinalizeQuizResultUseCaseTests()
    {
        sut = new FinalizeQuizResultUseCase(
            readerMock.Object,
            exporterMock.Object,
            NullLogger<FinalizeQuizResultUseCase>.Instance);
    }

    [Fact]
    public async Task FinalizeAsync_Should_Reexport_The_Reviewed_Result_To_The_Same_Workbook()
    {
        var reviewed = QuizResultWithOneAcceptedScan();
        readerMock
            .Setup(r => r.ReadAsync(WorkbookPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<QuizResult>.Success(reviewed));
        exporterMock
            .Setup(e => e.ExportAsync(reviewed, WorkbookPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await sut.FinalizeAsync(WorkbookPath, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        exporterMock.Verify(
            e => e.ExportAsync(reviewed, WorkbookPath, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FinalizeAsync_Should_Fail_And_Skip_Export_When_The_Import_Fails()
    {
        readerMock
            .Setup(r => r.ReadAsync(WorkbookPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<QuizResult>.Failure("Row 3: Status 'Acepted' is not one of ..."));

        var result = await sut.FinalizeAsync(WorkbookPath, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Row 3");
        exporterMock.Verify(
            e => e.ExportAsync(It.IsAny<QuizResult>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FinalizeAsync_Should_Propagate_An_Export_Failure()
    {
        readerMock
            .Setup(r => r.ReadAsync(WorkbookPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<QuizResult>.Success(QuizResultWithOneAcceptedScan()));
        exporterMock
            .Setup(e => e.ExportAsync(It.IsAny<QuizResult>(), WorkbookPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("disk full"));

        var result = await sut.FinalizeAsync(WorkbookPath, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("disk full");
    }

    private static QuizResult QuizResultWithOneAcceptedScan()
    {
        var result = new QuizResult();
        result.AddScan(new SheetScanResult(1, "R1/sheet.pdf", 12, 75, 0.9, ReviewStatus.Accepted, null));
        return result;
    }
}
