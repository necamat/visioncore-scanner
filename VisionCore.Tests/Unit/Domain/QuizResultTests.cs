using FluentAssertions;
using VisionCore.Domain.Entities;
using VisionCore.Domain.Imaging.Evaluation;
using VisionCore.Domain.Models;
using Xunit;

namespace VisionCore.Tests.Unit.Domain;

public sealed class QuizResultTests
{
    [Fact]
    public void GetStandings_Should_Sum_Accepted_Scores_Per_Team()
    {
        var quiz = new QuizResult();
        quiz.AddScan(AcceptedScan(round: 1, teamId: 1, score: 10));
        quiz.AddScan(AcceptedScan(round: 2, teamId: 1, score: 15));
        quiz.AddScan(AcceptedScan(round: 1, teamId: 2, score: 5));

        var standings = quiz.GetStandings().ToList();

        standings.Should().HaveCount(2);
        standings.Single(s => s.TeamId == 1).TotalScore.Should().Be(25);
        standings.Single(s => s.TeamId == 2).TotalScore.Should().Be(5);
    }

    [Fact]
    public void GetStandings_Should_Order_By_Total_Score_Descending()
    {
        var quiz = new QuizResult();
        quiz.AddScan(AcceptedScan(round: 1, teamId: 1, score: 5));
        quiz.AddScan(AcceptedScan(round: 1, teamId: 2, score: 10));

        var standings = quiz.GetStandings().ToList();

        standings.Should().HaveCount(2);
        standings[0].TeamId.Should().Be(2);
        standings[0].TotalScore.Should().BeGreaterThan(standings[1].TotalScore);
    }

    [Fact]
    public void GetStandings_Should_Ignore_Rejected_Scans()
    {
        var quiz = new QuizResult();
        quiz.AddScan(AcceptedScan(round: 1, teamId: 1, score: 10));
        quiz.AddScan(new SheetScanResult(1, "r1-t2.pdf", null, null, 0, ReviewStatus.Rejected, EvaluationFailureCode.RecognitionFailed));

        var standings = quiz.GetStandings().ToList();

        standings.Should().ContainSingle();
        standings[0].TeamId.Should().Be(1);
    }

    private static SheetScanResult AcceptedScan(int round, int teamId, int score) =>
        new(round, $"r{round}-t{teamId}.pdf", teamId, score, 0.95, ReviewStatus.Accepted, null);
}
