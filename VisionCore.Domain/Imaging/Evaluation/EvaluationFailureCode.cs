namespace VisionCore.Domain.Imaging.Evaluation;

public enum EvaluationFailureCode
{
    MissingDetectionResult,
    MissingGeometryResult,
    MissingRecognitionResult,
    InvalidGeometry,
    RecognitionFailed,
    LowConfidence
}
