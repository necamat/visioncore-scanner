namespace VisionCore.Tests.Integration.EndToEnd;

/// <summary>
/// Controlled degradations applied to a synthetic sheet render, modelling what
/// a real scanner produces: speckle noise (dust), faint ink (weak toner), JPEG
/// compression artefacts and digits printed off-centre in their cells. All
/// randomness is seeded so a given distortion is identical on every run.
/// </summary>
public sealed record SheetDistortion
{
    public static SheetDistortion None { get; } = new();

    /// <summary>Number of small dark speckles scattered over the page.</summary>
    public int NoiseSpeckles { get; init; }

    /// <summary>Ink intensity: 0 = solid black, higher = fainter print.</summary>
    public byte InkIntensity { get; init; }

    /// <summary>JPEG encode quality (100 = clean, lower = compression artefacts).</summary>
    public int JpegQuality { get; init; } = 100;

    /// <summary>Horizontal shift of every digit inside its cell, in pixels.</summary>
    public int DigitOffsetX { get; init; }

    /// <summary>Vertical shift of every digit inside its cell, in pixels.</summary>
    public int DigitOffsetY { get; init; }

    /// <summary>Seed for the speckle noise placement.</summary>
    public int RandomSeed { get; init; } = 20260610;

    /// <summary>
    /// When set, speckles are kept out of the digit cells: this models dust on
    /// the scanner glass *beside* the print. Dust that lands on a digit
    /// physically alters the glyph, so those cases belong to the
    /// safety-guarantee tests, not the must-read-exactly ones.
    /// </summary>
    public bool KeepSpecklesOffDigits { get; init; }
}
