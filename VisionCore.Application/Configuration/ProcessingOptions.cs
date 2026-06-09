namespace VisionCore.Application.Configuration;

/// <summary>
/// Console run settings (input location and Excel output) bound from the
/// "ProcessingOptions" configuration section.
/// </summary>
public sealed class ProcessingOptions
{
    /// <summary>Default input folder when none is passed on the command line.</summary>
    public string InputFolder { get; init; } = "./input";

    /// <summary>Folder the Excel report is written to.</summary>
    public string OutputFolder { get; init; } = "./output";

    /// <summary>File name of the generated Excel report.</summary>
    public string OutputFileName { get; init; } = "rezultati.xlsx";
}
