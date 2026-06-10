namespace VisionCore.Application.Configuration;

using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="PdfRegionOptions"/> including its nested
/// <see cref="PdfRegionBounds"/> rectangles. DataAnnotations validation does
/// not descend into nested objects, so without this an empty or mistyped
/// "PdfRegions" section would bind silently and every crop would degenerate to
/// a near-empty rectangle, rejecting all sheets with no hint of the cause.
/// </summary>
public sealed class PdfRegionOptionsValidator : IValidateOptions<PdfRegionOptions>
{
    private const int MinimumDpi = 50;
    private const int MaximumDpi = 1200;

    // A digit cell on the 200-DPI form is roughly 46x82 px; anything below
    // this floor is a configuration mistake, not a real region.
    private const int MinimumRegionSize = 8;
    private const int MaximumCoordinate = 20_000;

    public ValidateOptionsResult Validate(string? name, PdfRegionOptions options)
    {
        var failures = new List<string>();

        if (options.Dpi is < MinimumDpi or > MaximumDpi)
        {
            failures.Add($"Dpi must be between {MinimumDpi} and {MaximumDpi} but was {options.Dpi}.");
        }

        ValidateBounds(failures, nameof(options.TeamId), options.TeamId);
        ValidateBounds(failures, nameof(options.TeamIdDigit1), options.TeamIdDigit1);
        ValidateBounds(failures, nameof(options.TeamIdDigit2), options.TeamIdDigit2);
        ValidateBounds(failures, nameof(options.Score), options.Score);
        ValidateBounds(failures, nameof(options.ScoreDigit1), options.ScoreDigit1);
        ValidateBounds(failures, nameof(options.ScoreDigit2), options.ScoreDigit2);
        ValidateBounds(failures, nameof(options.ScoreDigit3), options.ScoreDigit3);

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static void ValidateBounds(List<string> failures, string regionName, PdfRegionBounds bounds)
    {
        if (bounds.X is < 0 or > MaximumCoordinate || bounds.Y is < 0 or > MaximumCoordinate)
        {
            failures.Add(
                $"PdfRegions:{regionName}: X and Y must be between 0 and {MaximumCoordinate} " +
                $"but were ({bounds.X}, {bounds.Y}).");
        }

        if (bounds.Width < MinimumRegionSize || bounds.Height < MinimumRegionSize)
        {
            failures.Add(
                $"PdfRegions:{regionName}: Width and Height must be at least {MinimumRegionSize} px " +
                $"but were ({bounds.Width}, {bounds.Height}). " +
                "Is the \"PdfRegions\" configuration section present and spelled correctly?");
        }
    }
}
