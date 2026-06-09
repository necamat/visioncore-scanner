using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using VisionCore.Application.Abstractions;
using VisionCore.Application.Imaging;
using VisionCore.Application.UseCases;
using VisionCore.Domain.Imaging.Evaluation;
using Xunit;

namespace VisionCore.Tests.Unit.Application;

public sealed class ScanQuizSheetsUseCaseTests : IDisposable
{
    private readonly string root;
    private readonly Mock<IScanSourceProvider> sourceProviderMock = new();
    private readonly Mock<IPipelineFactory> pipelineFactoryMock = new();
    private readonly ScanQuizSheetsUseCase sut;

    public ScanQuizSheetsUseCaseTests()
    {
        root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        sut = new ScanQuizSheetsUseCase(
            sourceProviderMock.Object,
            pipelineFactoryMock.Object,
            NullLogger<ScanQuizSheetsUseCase>.Instance);
    }

    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public async Task ScanAsync_Should_Fail_When_Root_Does_Not_Exist()
    {
        var result = await sut.ScanAsync(Path.Combine(root, "missing"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Root folder does not exist");
    }

    [Fact]
    public async Task ScanAsync_Should_Process_Every_Source_From_The_Provider()
    {
        var s1 = new ScanSource(1, "R1/a.pdf");
        var s2 = new ScanSource(2, "R2/b.pdf");
        SetupSources(s1, s2);
        SetupPipelineFor(s1.SourcePath, success: true);
        SetupPipelineFor(s2.SourcePath, success: true);

        var result = await sut.ScanAsync(root, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.GetScans().Should().HaveCount(2);
        pipelineFactoryMock.Verify(f => f.CreateForSource(s1.SourcePath), Times.Once);
        pipelineFactoryMock.Verify(f => f.CreateForSource(s2.SourcePath), Times.Once);
    }

    [Fact]
    public async Task ScanAsync_Should_Record_Rejected_And_Continue_When_A_Source_Throws()
    {
        var bad = new ScanSource(1, "R1/bad.pdf");
        var good = new ScanSource(1, "R1/good.pdf");
        SetupSources(bad, good);
        pipelineFactoryMock.Setup(f => f.CreateForSource(bad.SourcePath))
            .Throws(new InvalidOperationException("corrupt"));
        SetupPipelineFor(good.SourcePath, success: true);

        var result = await sut.ScanAsync(root, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.GetScans().Should().HaveCount(2);
        pipelineFactoryMock.Verify(f => f.CreateForSource(good.SourcePath), Times.Once);
    }

    [Fact]
    public async Task ScanAsync_Should_Propagate_Cancellation()
    {
        var source = new ScanSource(1, "R1/a.pdf");
        SetupSources(source);
        using var cts = new CancellationTokenSource();
        pipelineFactoryMock.Setup(f => f.CreateForSource(source.SourcePath))
            .Callback(cts.Cancel)
            .Returns(Mock.Of<IImageProcessingPipeline>());

        var act = () => sut.ScanAsync(root, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ScanAsync_Should_Fail_When_Source_Enumeration_Throws()
    {
        sourceProviderMock.Setup(p => p.GetSources(It.IsAny<string>()))
            .Throws(new InvalidOperationException("disk error"));

        var result = await sut.ScanAsync(root, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("disk error");
    }

    private void SetupSources(params ScanSource[] sources) =>
        sourceProviderMock.Setup(p => p.GetSources(It.IsAny<string>())).Returns(sources);

    private void SetupPipelineFor(string path, bool success)
    {
        var pipeline = new Mock<IImageProcessingPipeline>();
        pipeline.Setup(p => p.ProcessAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildResult(success));
        pipelineFactoryMock.Setup(f => f.CreateForSource(path)).Returns(pipeline.Object);
    }

    private static PipelineResult BuildResult(bool success) =>
        new(
            success,
            success ? null : "error",
            success ? 1 : null,
            success ? 42 : null,
            success ? 0.91 : 0.0,
            success ? "1:042" : null,
            null,
            null,
            null,
            success ? ReviewStatus.Accepted : ReviewStatus.Rejected,
            null);
}
