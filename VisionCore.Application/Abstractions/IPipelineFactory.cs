namespace VisionCore.Application.Abstractions;

/// <summary>
/// Selects and assembles the correct IImageProcessingPipeline
/// based on the source file format.
/// </summary>
public interface IPipelineFactory
{
    /// <summary>
    /// Returns a pipeline configured for the given source file.
    /// </summary>
    /// <param name="sourcePath">Path to the file to process.</param>
    /// <exception cref="NotSupportedException">
    /// Thrown when no pipeline is registered for the file extension.
    /// </exception>
    IImageProcessingPipeline CreateForSource(string sourcePath);
}
