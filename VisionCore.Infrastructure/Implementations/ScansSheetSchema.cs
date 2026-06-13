namespace VisionCore.Infrastructure.Implementations;

/// <summary>
/// The single source of truth for the layout of the "Scans" worksheet, shared
/// by <see cref="ClosedXmlExcelExporter"/> (which writes it) and
/// <see cref="ClosedXmlReviewedScansReader"/> (which reads it back after a human
/// review). Keeping the sheet name, column positions and header labels in one
/// place means a column can never be added, renamed or reordered in the writer
/// without the reader following — the two would otherwise drift apart silently
/// and the reader would misinterpret the standings.
/// </summary>
internal static class ScansSheetSchema
{
    public const string SheetName = "Scans";

    public const int HeaderRow = 1;
    public const int FirstDataRow = 2;

    public const int ColumnRound = 1;
    public const int ColumnSource = 2;
    public const int ColumnTeamId = 3;
    public const int ColumnScore = 4;
    public const int ColumnConfidence = 5;
    public const int ColumnStatus = 6;
    public const int ColumnFailure = 7;

    /// <summary>The last column the schema defines — the bound for row scans.</summary>
    public const int LastColumn = ColumnFailure;

    public const string HeaderRound = "Round";
    public const string HeaderSource = "Source Path";
    public const string HeaderTeamId = "Team ID";
    public const string HeaderScore = "Score";
    public const string HeaderConfidence = "Confidence";
    public const string HeaderStatus = "Status";
    public const string HeaderFailure = "Failure";

    /// <summary>
    /// The header labels in column order (starting at <see cref="ColumnRound"/>),
    /// so the reader can confirm a workbook actually carries this layout before
    /// trusting the column positions.
    /// </summary>
    public static readonly IReadOnlyList<string> HeaderLabels =
    [
        HeaderRound,
        HeaderSource,
        HeaderTeamId,
        HeaderScore,
        HeaderConfidence,
        HeaderStatus,
        HeaderFailure
    ];
}
