namespace VisionCore.Domain.Imaging.Recognition;

public sealed record DigitRecognitionResult(
    bool IsSuccess,
    RecognitionFailureCode? Failure,
    RecognizedNumber? TeamId,
    RecognizedNumber? Score,
    float GlobalConfidence)
{
    public static DigitRecognitionResult Success(
        RecognizedNumber teamId,
        RecognizedNumber score,
        float globalConfidence) =>
        new(true, null, teamId, score, globalConfidence);

    public static DigitRecognitionResult FailureResult(
        RecognitionFailureCode failure,
        RecognizedNumber? teamId,
        RecognizedNumber? score,
        float globalConfidence) =>
        new(false, failure, teamId, score, globalConfidence);
}
