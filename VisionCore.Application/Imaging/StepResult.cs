namespace VisionCore.Application.Imaging;

/// <summary>
/// Outcome of a single pipeline step: continue to the next step, or stop the run
/// with a failure reason. Lets the runner own control flow instead of steps
/// mutating shared success/error flags.
/// </summary>
public sealed record StepResult
{
    private StepResult(bool shouldContinue, string? error)
    {
        ShouldContinue = shouldContinue;
        Error = error;
    }

    public bool ShouldContinue { get; }

    public string? Error { get; }

    public static StepResult Continue { get; } = new(true, null);

    public static StepResult Fail(string error) => new(false, error);
}
