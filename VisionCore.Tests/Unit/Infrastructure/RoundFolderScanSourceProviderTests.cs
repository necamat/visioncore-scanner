using FluentAssertions;
using Microsoft.Extensions.Options;
using VisionCore.Application.Configuration;
using VisionCore.Infrastructure.Implementations;
using Xunit;

namespace VisionCore.Tests.Unit.Infrastructure;

public sealed class RoundFolderScanSourceProviderTests : IDisposable
{
    private readonly string root;
    private readonly RoundFolderScanSourceProvider sut = new(
        Options.Create(new ScanSourceOptions { RoundFolderPrefix = "R", SearchPatterns = ["*.pdf"] }));

    public RoundFolderScanSourceProviderTests()
    {
        root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void GetSources_Should_Order_Rounds_Numerically_Not_Lexically()
    {
        // An ordinal string sort would yield R1, R10, R2 — the standings and
        // the report must follow the natural round order instead.
        CreateRoundFile(2, "a.pdf");
        CreateRoundFile(10, "b.pdf");
        CreateRoundFile(1, "c.pdf");

        var sources = sut.GetSources(root).ToList();

        sources.Select(source => source.Round).Should().Equal(1, 2, 10);
    }

    [Fact]
    public void GetSources_Should_Order_Files_Within_A_Round_Deterministically()
    {
        CreateRoundFile(1, "b.pdf");
        CreateRoundFile(1, "a.pdf");
        CreateRoundFile(1, "c.pdf");

        var sources = sut.GetSources(root).ToList();

        sources.Select(source => Path.GetFileName(source.SourcePath)).Should().Equal("a.pdf", "b.pdf", "c.pdf");
    }

    [Fact]
    public void GetSources_Should_Skip_Folders_That_Do_Not_Match_The_Round_Pattern()
    {
        CreateRoundFile(1, "a.pdf");
        Directory.CreateDirectory(Path.Combine(root, "output"));
        Directory.CreateDirectory(Path.Combine(root, "Rx"));

        var sources = sut.GetSources(root).ToList();

        sources.Should().ContainSingle().Which.Round.Should().Be(1);
    }

    [Fact]
    public void GetSources_Should_Return_Empty_For_A_Missing_Root()
    {
        sut.GetSources(Path.Combine(root, "missing")).Should().BeEmpty();
    }

    private void CreateRoundFile(int round, string fileName)
    {
        var folder = Path.Combine(root, $"R{round}");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, fileName), "pdf");
    }
}
