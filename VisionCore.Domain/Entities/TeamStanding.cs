namespace VisionCore.Domain.Entities;

/// <summary>A team's accumulated score across all accepted scans.</summary>
public sealed record TeamStanding(int TeamId, int TotalScore);
