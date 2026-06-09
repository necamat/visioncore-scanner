using FluentAssertions;
using Moq;
using VisionCore.Application.Abstractions;
using VisionCore.Application.Imaging;
using Xunit;

namespace VisionCore.Tests.Unit.Application;

public sealed class StepPipelineTests
{
    [Fact]
    public void Constructor_Should_Throw_When_No_Steps_Provided()
    {
        var act = () => new StepPipeline(Array.Empty<IImageProcessingStep>());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Constructor_Should_Throw_When_Duplicate_Stages_Provided()
    {
        var act = () => new StepPipeline([Step(PipelineStage.CropRegions), Step(PipelineStage.CropRegions)]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task ProcessAsync_Should_Run_Steps_In_Stage_Order()
    {
        var executed = new List<PipelineStage>();
        var recognize = Step(PipelineStage.RecognizeDigits, ctx => executed.Add(PipelineStage.RecognizeDigits));
        var crop = Step(PipelineStage.CropRegions, ctx => executed.Add(PipelineStage.CropRegions));

        var pipeline = new StepPipeline([recognize, crop]);
        await pipeline.ProcessAsync("sheet.pdf", CancellationToken.None);

        executed.Should().Equal(PipelineStage.CropRegions, PipelineStage.RecognizeDigits);
    }

    [Fact]
    public async Task ProcessAsync_Should_Stop_At_First_Failing_Step()
    {
        var executed = new List<PipelineStage>();
        var crop = Step(PipelineStage.CropRegions, _ => executed.Add(PipelineStage.CropRegions), StepResult.Fail("boom"));
        var recognize = Step(PipelineStage.RecognizeDigits, _ => executed.Add(PipelineStage.RecognizeDigits));

        var pipeline = new StepPipeline([crop, recognize]);
        var result = await pipeline.ProcessAsync("sheet.pdf", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("boom");
        executed.Should().Equal(PipelineStage.CropRegions);
    }

    private static IImageProcessingStep Step(
        PipelineStage stage,
        Action<PipelineContext>? onExecute = null,
        StepResult? outcome = null)
    {
        var mock = new Mock<IImageProcessingStep>();
        mock.Setup(s => s.Stage).Returns(stage);
        mock
            .Setup(s => s.ExecuteAsync(It.IsAny<PipelineContext>(), It.IsAny<CancellationToken>()))
            .Returns<PipelineContext, CancellationToken>((ctx, _) =>
            {
                onExecute?.Invoke(ctx);
                return Task.FromResult(outcome ?? StepResult.Continue);
            });
        return mock.Object;
    }
}
