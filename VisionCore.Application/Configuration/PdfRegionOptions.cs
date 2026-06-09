namespace VisionCore.Application.Configuration;

/// <summary>
/// Configuration for fixed-coordinate PDF region extraction.
/// Coordinates are in pixels for a page rendered at the configured DPI
/// (default: 200 DPI → A4 ≈ 1654 × 2339 px).
///
/// Bind from appsettings.json section "PdfRegions".
/// </summary>
public sealed class PdfRegionOptions
{
    /// <summary>DPI used when rendering the PDF page to a bitmap.</summary>
    public int Dpi { get; init; } = 200;

    /// <summary>TeamId container box (whole number area).</summary>
    public PdfRegionBounds TeamId { get; init; } = new();

    /// <summary>First digit of TeamId.</summary>
    public PdfRegionBounds TeamIdDigit1 { get; init; } = new();

    /// <summary>Second digit of TeamId.</summary>
    public PdfRegionBounds TeamIdDigit2 { get; init; } = new();

    /// <summary>Score container box (sum of the three score digits).</summary>
    public PdfRegionBounds Score { get; init; } = new();

    /// <summary>First digit of Score (hundreds).</summary>
    public PdfRegionBounds ScoreDigit1 { get; init; } = new();

    /// <summary>Second digit of Score (tens).</summary>
    public PdfRegionBounds ScoreDigit2 { get; init; } = new();

    /// <summary>Third digit of Score (units).</summary>
    public PdfRegionBounds ScoreDigit3 { get; init; } = new();
}

/// <summary>Pixel rectangle for one form region on a rendered PDF page.</summary>
public sealed class PdfRegionBounds
{
    /// <summary>Left edge in pixels from the left of the rendered page.</summary>
    public int X { get; init; }

    /// <summary>Top edge in pixels from the top of the rendered page.</summary>
    public int Y { get; init; }

    /// <summary>Width in pixels.</summary>
    public int Width { get; init; } = 1;

    /// <summary>Height in pixels.</summary>
    public int Height { get; init; } = 1;
}
