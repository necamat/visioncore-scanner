namespace VisionCore.Domain.Imaging.Recognition;

public sealed record NumberRecognitionResult(
    bool IsSuccess,
    RecognitionFailureCode? Failure,
    RecognizedNumber? Number,
    float Confidence)
{
    public static NumberRecognitionResult Success(RecognizedNumber number) =>
        new(true, null, number, number.Confidence);

    public static NumberRecognitionResult FailureResult(
        RecognitionFailureCode failure,
        RecognizedNumber? number,
        float confidence) =>
        new(false, failure, number, confidence);
}
