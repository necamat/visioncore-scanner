using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VisionCore.Application.Abstractions;
using VisionCore.Domain.Entities;
using VisionCore.Domain.Imaging.Evaluation;
using VisionCore.Infrastructure.Implementations;
using Xunit;

namespace VisionCore.Tests.Unit.Infrastructure;

public sealed class JsonProcessingStateRepositoryTests : IDisposable
{
    private readonly string root;
    private readonly JsonProcessingStateRepository sut =
        new(NullLogger<JsonProcessingStateRepository>.Instance);

    public JsonProcessingStateRepositoryTests()
    {
        root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public async Task LoadAsync_Should_Return_Empty_State_When_No_File_Exists()
    {
        var state = await sut.LoadAsync(root, CancellationToken.None);

        state.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_Then_LoadAsync_Should_Round_Trip_The_State()
    {
        var saved = new RoundProcessingState(
            Round: 3,
            Sources: [new SourceFingerprint("R3/sheet.pdf", 1024, new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc))],
            Results:
            [
                new SheetScanResult(3, "R3/sheet.pdf", 12, 75, 0.91, ReviewStatus.Accepted, null),
                new SheetScanResult(3, "R3/bad.pdf", null, null, 0.10, ReviewStatus.Rejected, EvaluationFailureCode.LowConfidence)
            ]);

        await sut.SaveAsync(root, [saved], CancellationToken.None);
        var loaded = await sut.LoadAsync(root, CancellationToken.None);

        loaded.Should().ContainKey(3);
        loaded[3].Sources.Should().Equal(saved.Sources);
        loaded[3].Results.Should().Equal(saved.Results);
    }

    [Fact]
    public async Task LoadAsync_Should_Return_Empty_State_For_A_Corrupt_File()
    {
        await File.WriteAllTextAsync(Path.Combine(root, ".visioncore-state.json"), "{not valid json!");

        var state = await sut.LoadAsync(root, CancellationToken.None);

        state.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_Should_Return_Empty_State_For_An_Unsupported_Version()
    {
        await File.WriteAllTextAsync(
            Path.Combine(root, ".visioncore-state.json"),
            """{"Version": 999, "Rounds": []}""");

        var state = await sut.LoadAsync(root, CancellationToken.None);

        state.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_Should_Overwrite_The_Previous_State()
    {
        var first = new RoundProcessingState(1, [], []);
        var second = new RoundProcessingState(2, [], []);

        await sut.SaveAsync(root, [first], CancellationToken.None);
        await sut.SaveAsync(root, [second], CancellationToken.None);
        var loaded = await sut.LoadAsync(root, CancellationToken.None);

        loaded.Should().ContainKey(2);
        loaded.Should().NotContainKey(1);
    }
}
