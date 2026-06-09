namespace VisionCore.Domain.Models;

using System.Collections.Generic;
using System.Linq;
using VisionCore.Domain.Entities;
using VisionCore.Domain.Imaging.Evaluation;

/// <summary>
/// Aggregates the per-sheet scan results of a scanning run and derives the
/// team standings from them.
/// </summary>
public sealed class QuizResult
{
    private readonly List<SheetScanResult> _scans = [];

    /// <summary>Records the result of a single scanned sheet.</summary>
    public void AddScan(SheetScanResult result)
    {
        _scans.Add(result);
    }

    /// <summary>Returns every recorded scan, in the order it was added.</summary>
    public IReadOnlyCollection<SheetScanResult> GetScans()
    {
        return _scans.AsReadOnly();
    }

    /// <summary>
    /// Returns the team standings, ordered by total score descending. Only
    /// <see cref="ReviewStatus.Accepted"/> scans contribute: scans flagged
    /// <see cref="ReviewStatus.NeedsReview"/> or <see cref="ReviewStatus.Rejected"/>
    /// are excluded until a human confirms them, so an unconfirmed team will not
    /// appear here even if its digits were read correctly.
    /// </summary>
    public IReadOnlyCollection<TeamStanding> GetStandings()
    {
        var standings = _scans
            .Where(scan => scan.Status == ReviewStatus.Accepted)
            .Where(scan => scan.TeamId.HasValue && scan.Score.HasValue)
            .GroupBy(scan => scan.TeamId!.Value)
            .Select(group => new TeamStanding(group.Key, group.Sum(item => item.Score!.Value)))
            .OrderByDescending(standing => standing.TotalScore)
            .ToList();

        return standings.AsReadOnly();
    }
}
