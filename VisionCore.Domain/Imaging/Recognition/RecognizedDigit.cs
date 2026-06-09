namespace VisionCore.Domain.Imaging.Recognition;

using VisionCore.Domain.Imaging.Layout;

/// <summary>A single recognized decimal digit (0-9) for a form region.</summary>
public sealed record RecognizedDigit
{
    public RecognizedDigit(FormRegion region, int value, float confidence)
    {
        if (value is < 0 or > 9)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), value, "A recognized digit must be between 0 and 9.");
        }

        Region = region;
        Value = value;
        Confidence = confidence;
    }

    public FormRegion Region { get; }

    public int Value { get; }

    public float Confidence { get; }
}
