using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using VisionCore.Application.Abstractions;
using VisionCore.Application.Configuration;
using VisionCore.Application.Imaging;
using VisionCore.Application.UseCases;
using VisionCore.Domain.Entities;
using VisionCore.Domain.Imaging.Evaluation;
using Xunit;

namespace VisionCore.Tests.Unit.Application;

public sealed class ScanQuizSheetsUseCaseTests : IDisposable
{
    private readonly string root;
    private readonly Mock<IScanSourceProvider> sourceProviderMock = new();
    private readonly Mock<IPipelineFactory> pipelineFactoryMock = new();
    private readonly Mock<IProcessingStateRepository> stateRepositoryMock = new();
    private readonly ScanQuizSheetsUseCase sut;

    public ScanQuizSheetsUseCaseTests()
    {
        root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        stateRepositoryMock
            .Setup(r => r.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, RoundProcessingState>());

        sut = CreateUseCase(new ProcessingOptions());
    }

    private ScanQuizSheetsUseCase CreateUseCase(ProcessingOptions options) =>
        new(
            sourceProviderMock.Object,
            pipelineFactoryMock.Object,
            stateRepositoryMock.Object,
            Options.Create(options),
            NullLogger<ScanQuizSheetsUseCase>.Instance);

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

    [Fact]
    public async Task ScanAsync_Should_Keep_Results_In_Provider_Order_When_Scanning_Concurrently()
    {
        // Earlier sources finish later: if results were collected by completion
        // order instead of provider order, the rounds below would come back
        // reversed.
        var sources = Enumerable.Range(1, 6)
            .Select(round => new ScanSource(round, $"R{round}/sheet.pdf"))
            .ToArray();
        SetupSources(sources);
        for (var index = 0; index < sources.Length; index++)
        {
            SetupDelayedPipelineFor(sources[index].SourcePath, delay: TimeSpan.FromMilliseconds((6 - index) * 25));
        }

        var result = await sut.ScanAsync(root, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.GetScans().Select(scan => scan.Round).Should().Equal(1, 2, 3, 4, 5, 6);
    }

    [Fact]
    public async Task ScanAsync_Should_Process_Sequentially_When_Parallelism_Is_Limited_To_One()
    {
        var limitedSut = CreateUseCase(new ProcessingOptions { MaxDegreeOfParallelism = 1 });
        var concurrent = 0;
        var maxObservedConcurrency = 0;
        var sources = Enumerable.Range(1, 4)
            .Select(round => new ScanSource(round, $"R{round}/sheet.pdf"))
            .ToArray();
        SetupSources(sources);
        foreach (var source in sources)
        {
            var pipeline = new Mock<IImageProcessingPipeline>();
            pipeline.Setup(p => p.ProcessAsync(source.SourcePath, It.IsAny<CancellationToken>()))
                .Returns(async (string _, CancellationToken token) =>
                {
                    var now = Interlocked.Increment(ref concurrent);
                    InterlockedExtensionsMax(ref maxObservedConcurrency, now);
                    await Task.Delay(TimeSpan.FromMilliseconds(20), token);
                    Interlocked.Decrement(ref concurrent);
                    return BuildResult(success: true);
                });
            pipelineFactoryMock.Setup(f => f.CreateForSource(source.SourcePath)).Returns(pipeline.Object);
        }

        var result = await limitedSut.ScanAsync(root, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        maxObservedConcurrency.Should().Be(1, "MaxDegreeOfParallelism = 1 must serialize the scans");
    }

    [Fact]
    public async Task ScanAsync_Should_Reuse_Persisted_Results_For_An_Unchanged_Round()
    {
        var source = CreateRealSource(round: 1, "R1", "a.pdf");
        SetupSources(source);
        var cachedScan = new SheetScanResult(1, source.SourcePath, 7, 120, 0.95, ReviewStatus.Accepted, null);
        SetupPersistedState(new RoundProcessingState(1, [FingerprintOf(source)], [cachedScan]));

        var result = await sut.ScanAsync(root, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.GetScans().Should().ContainSingle().Which.Should().Be(cachedScan);
        pipelineFactoryMock.Verify(
            f => f.CreateForSource(It.IsAny<string>()), Times.Never,
            "an unchanged round must not be scanned again");
    }

    [Fact]
    public async Task ScanAsync_Should_Rescan_A_Round_Whose_Files_Changed()
    {
        var source = CreateRealSource(round: 1, "R1", "a.pdf");
        SetupSources(source);
        var staleFingerprint = FingerprintOf(source) with { FileSizeBytes = 12345 };
        var cachedScan = new SheetScanResult(1, source.SourcePath, 7, 120, 0.95, ReviewStatus.Accepted, null);
        SetupPersistedState(new RoundProcessingState(1, [staleFingerprint], [cachedScan]));
        SetupPipelineFor(source.SourcePath, success: true);

        var result = await sut.ScanAsync(root, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        pipelineFactoryMock.Verify(f => f.CreateForSource(source.SourcePath), Times.Once);
    }

    [Fact]
    public async Task ScanAsync_Should_Ignore_Persisted_State_When_Reuse_Is_Disabled()
    {
        var forcedSut = CreateUseCase(new ProcessingOptions { ReuseUnchangedRounds = false });
        var source = CreateRealSource(round: 1, "R1", "a.pdf");
        SetupSources(source);
        SetupPipelineFor(source.SourcePath, success: true);

        var result = await forcedSut.ScanAsync(root, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        stateRepositoryMock.Verify(
            r => r.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        pipelineFactoryMock.Verify(f => f.CreateForSource(source.SourcePath), Times.Once);
    }

    [Fact]
    public async Task ScanAsync_Should_Persist_The_State_Of_Every_Round()
    {
        var first = CreateRealSource(round: 1, "R1", "a.pdf");
        var second = CreateRealSource(round: 2, "R2", "b.pdf");
        SetupSources(first, second);
        SetupPipelineFor(first.SourcePath, success: true);
        SetupPipelineFor(second.SourcePath, success: true);

        await sut.ScanAsync(root, CancellationToken.None);

        stateRepositoryMock.Verify(
            r => r.SaveAsync(
                root,
                It.Is<IReadOnlyCollection<RoundProcessingState>>(states =>
                    states.Count == 2 &&
                    states.Any(state => state.Round == 1) &&
                    states.Any(state => state.Round == 2)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private ScanSource CreateRealSource(int round, string roundFolder, string fileName)
    {
        var folder = Path.Combine(root, roundFolder);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);
        File.WriteAllText(path, "test-pdf-content");
        return new ScanSource(round, path);
    }

    private SourceFingerprint FingerprintOf(ScanSource source)
    {
        var info = new FileInfo(source.SourcePath);
        return new SourceFingerprint(
            Path.GetRelativePath(root, source.SourcePath), info.Length, info.LastWriteTimeUtc);
    }

    private void SetupPersistedState(params RoundProcessingState[] states) =>
        stateRepositoryMock
            .Setup(r => r.LoadAsync(root, It.IsAny<CancellationToken>()))
            .ReturnsAsync(states.ToDictionary(state => state.Round));

    private static void InterlockedExtensionsMax(ref int target, int candidate)
    {
        int current;
        while (candidate > (current = Volatile.Read(ref target)) &&
               Interlocked.CompareExchange(ref target, candidate, current) != current)
        {
        }
    }

    private void SetupDelayedPipelineFor(string path, TimeSpan delay)
    {
        var pipeline = new Mock<IImageProcessingPipeline>();
        pipeline.Setup(p => p.ProcessAsync(path, It.IsAny<CancellationToken>()))
            .Returns(async (string _, CancellationToken token) =>
            {
                await Task.Delay(delay, token);
                return BuildResult(success: true);
            });
        pipelineFactoryMock.Setup(f => f.CreateForSource(path)).Returns(pipeline.Object);
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
