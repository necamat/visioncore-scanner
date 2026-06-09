namespace VisionCore.Domain.Imaging.Recognition;

public sealed class RecognizedNumber
{
    public RecognizedNumber(IReadOnlyList<RecognizedDigit> digits)
    {
        if (digits.Count == 0)
        {
            throw new InvalidOperationException("At least one recognized digit is required.");
        }

        Digits = digits;
        Confidence = digits.Average(digit => digit.Confidence);
        Text = string.Concat(digits.Select(digit => digit.Value));

        // Build the value arithmetically from validated 0-9 digits; avoids a
        // string round-trip and any int.Parse overflow on long inputs.
        var value = 0;
        foreach (var digit in digits)
        {
            value = checked((value * 10) + digit.Value);
        }

        Value = value;
    }

    public IReadOnlyList<RecognizedDigit> Digits { get; }

    public string Text { get; }

    public int Value { get; }

    public float Confidence { get; }
}
