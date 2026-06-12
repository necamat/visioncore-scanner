using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VisionCore.Application.Abstractions;
using VisionCore.Application.Configuration;
using VisionCore.Domain.Entities;
using VisionCore.Domain.Imaging.Evaluation;
using VisionCore.Infrastructure.Implementations;
using Xunit;

namespace VisionCore.Tests.Unit.Infrastructure;

public sealed class JsonProcessingStateRepositoryTests : IDisposable
{
    private readonly string root;
    private readonly JsonProcessingStateRepository sut = CreateRepository(new DigitRecognitionOptions());

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
            """{"Version": 999, "ConfigurationFingerprint": "X", "Rounds": []}""");

        var state = await sut.LoadAsync(root, CancellationToken.None);

        state.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_Should_Discard_State_Written_Under_A_Different_Configuration()
    {
        // Results produced with different recognition tunables must never be
        // reused: the cached statuses and confidences would no longer reflect
        // what the current configuration would decide.
        await sut.SaveAsync(root, [new RoundProcessingState(1, [], [])], CancellationToken.None);

        var reconfigured = CreateRepository(new DigitRecognitionOptions { DarkPixelThreshold = 99 });
        var state = await reconfigured.LoadAsync(root, CancellationToken.None);

        state.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_Should_Keep_The_First_Of_Duplicate_Round_Entries()
    {
        // A hand-edited or merged state file may carry the same round twice;
        // the cache must degrade gracefully, never crash the run.
        var fingerprint = await SavedConfigurationFingerprintAsync();
        await File.WriteAllTextAsync(
            Path.Combine(root, ".visioncore-state.json"),
            $$"""
            {
              "Version": 2,
              "ConfigurationFingerprint": "{{fingerprint}}",
              "Rounds": [
                { "Round": 1, "Sources": [], "Results": [ { "Round": 1, "SourcePath": "a.pdf", "TeamId": 7, "Score": 10, "Confidence": 0.9, "Status": "Accepted", "FailureCode": null } ] },
                { "Round": 1, "Sources": [], "Results": [ { "Round": 1, "SourcePath": "b.pdf", "TeamId": 8, "Score": 20, "Confidence": 0.9, "Status": "Accepted", "FailureCode": null } ] }
              ]
            }
            """);

        var state = await sut.LoadAsync(root, CancellationToken.None);

        state.Should().ContainKey(1);
        state[1].Results.Single().SourcePath.Should().Be("a.pdf", "the first occurrence wins");
    }

    [Fact]
    public async Task LoadAsync_Should_Skip_A_Round_With_An_Unknown_Status_And_Keep_The_Rest()
    {
        var fingerprint = await SavedConfigurationFingerprintAsync();
        await File.WriteAllTextAsync(
            Path.Combine(root, ".visioncore-state.json"),
            $$"""
            {
              "Version": 2,
              "ConfigurationFingerprint": "{{fingerprint}}",
              "Rounds": [
                { "Round": 1, "Sources": [], "Results": [ { "Round": 1, "SourcePath": "a.pdf", "TeamId": 7, "Score": 10, "Confidence": 0.9, "Status": "SomethingNew", "FailureCode": null } ] },
                { "Round": 2, "Sources": [], "Results": [ { "Round": 2, "SourcePath": "b.pdf", "TeamId": 8, "Score": 20, "Confidence": 0.9, "Status": "Accepted", "FailureCode": null } ] }
              ]
            }
            """);

        var state = await sut.LoadAsync(root, CancellationToken.None);

        state.Should().NotContainKey(1, "a round with an unknown status is dropped rather than guessed");
        state.Should().ContainKey(2);
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

    [Fact]
    public async Task SaveAsync_Should_Not_Leave_A_Temp_File_Behind()
    {
        await sut.SaveAsync(root, [new RoundProcessingState(1, [], [])], CancellationToken.None);

        Directory.GetFiles(root, "*.tmp").Should().BeEmpty("the save must move the temp file into place");
    }

    private static JsonProcessingStateRepository CreateRepository(DigitRecognitionOptions digitOptions) =>
        new(
            Options.Create(digitOptions),
            Options.Create(new ConfidenceEvaluationOptions()),
            Options.Create(new PdfRegionOptions()),
            NullLogger<JsonProcessingStateRepository>.Instance);

    /// <summary>Saves an empty state with the sut and reads back the fingerprint it wrote.</summary>
    private async Task<string> SavedConfigurationFingerprintAsync()
    {
        await sut.SaveAsync(root, [], CancellationToken.None);
        var json = await File.ReadAllTextAsync(Path.Combine(root, ".visioncore-state.json"));
        var document = System.Text.Json.JsonDocument.Parse(json);
        return document.RootElement.GetProperty("ConfigurationFingerprint").GetString()!;
    }
}
