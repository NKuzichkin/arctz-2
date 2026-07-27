using System.Collections.Generic;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Device.Commands;

namespace ArctZ.Tests.Services.Device;

public class BufferAwareCommandQueueTests
{
    [Fact]
    public void EnqueueAsync_MultipleShortLines_PipelinesWithoutWaitingForAck()
    {
        var transport = new FakeDeviceTransport();
        var queue = new BufferAwareCommandQueue(transport);

        _ = queue.EnqueueAsync(new GCodeLineCommand("G1 X1"));
        _ = queue.EnqueueAsync(new GCodeLineCommand("G1 X2"));
        _ = queue.EnqueueAsync(new GCodeLineCommand("G1 X3"));

        Assert.Equal(new[] { "G1 X1", "G1 X2", "G1 X3" }, transport.SentLines);
    }

    [Fact]
    public void EnqueueAsync_LinesExceedingCapacity_OnlySendsWhatFitsThenSendsRestAfterAck()
    {
        var transport = new FakeDeviceTransport();
        var queue = new BufferAwareCommandQueue(transport);
        queue.UpdateBufferCapacity(rxBytesAvailable: 10, plannerBlocksAvailable: 15);

        _ = queue.EnqueueAsync(new GCodeLineCommand("G1 X1"));
        _ = queue.EnqueueAsync(new GCodeLineCommand("G1 X2"));

        Assert.Equal(new[] { "G1 X1" }, transport.SentLines);

        queue.HandleOk();

        Assert.Equal(new[] { "G1 X1", "G1 X2" }, transport.SentLines);
    }

    [Fact]
    public async Task HandleOk_CompletesInFlightCommandAcknowledged()
    {
        var transport = new FakeDeviceTransport();
        var queue = new BufferAwareCommandQueue(transport);
        var resultTask = queue.EnqueueAsync(new GCodeLineCommand("G1 X1"));

        queue.HandleOk();
        var result = await resultTask;

        Assert.Equal(CommandOutcome.Acknowledged, result.Outcome);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task HandleError_RejectsInFlightAndAbortsPendingNotYetSent()
    {
        var transport = new FakeDeviceTransport();
        var queue = new BufferAwareCommandQueue(transport);
        queue.UpdateBufferCapacity(rxBytesAvailable: 10, plannerBlocksAvailable: 15);

        var taskA = queue.EnqueueAsync(new GCodeLineCommand("G1 X1"));
        var taskB = queue.EnqueueAsync(new GCodeLineCommand("G1 X2"));
        var taskC = queue.EnqueueAsync(new GCodeLineCommand("G1 X3"));

        Assert.Equal(new[] { "G1 X1" }, transport.SentLines);

        queue.HandleError(9);

        var resultA = await taskA;
        var resultB = await taskB;
        var resultC = await taskC;

        Assert.Equal(CommandOutcome.Rejected, resultA.Outcome);
        Assert.Equal(9, resultA.ErrorCode);
        Assert.Equal(CommandOutcome.Aborted, resultB.Outcome);
        Assert.Null(resultB.ErrorCode);
        Assert.Equal(CommandOutcome.Aborted, resultC.Outcome);
        Assert.Equal(new[] { "G1 X1" }, transport.SentLines);
    }

    [Fact]
    public void HandleError_RaisesCommandCompletedForEachAffectedCommand()
    {
        var transport = new FakeDeviceTransport();
        var queue = new BufferAwareCommandQueue(transport);
        queue.UpdateBufferCapacity(rxBytesAvailable: 10, plannerBlocksAvailable: 15);
        var completed = new List<(GCodeLineCommand Command, CommandResult Result)>();
        queue.CommandCompleted += (command, result) => completed.Add((command, result));

        _ = queue.EnqueueAsync(new GCodeLineCommand("G1 X1"));
        _ = queue.EnqueueAsync(new GCodeLineCommand("G1 X2"));

        queue.HandleError(9);

        Assert.Equal(2, completed.Count);
        Assert.Equal("G1 X1", completed[0].Command.Line);
        Assert.Equal(CommandOutcome.Rejected, completed[0].Result.Outcome);
        Assert.Equal("G1 X2", completed[1].Command.Line);
        Assert.Equal(CommandOutcome.Aborted, completed[1].Result.Outcome);
    }

    [Fact]
    public void Enqueue_ExclusiveDollarCommand_WaitsForQueueToDrainBeforeSending()
    {
        var transport = new FakeDeviceTransport();
        var queue = new BufferAwareCommandQueue(transport);

        _ = queue.EnqueueAsync(new GCodeLineCommand("G1 X1"));
        _ = queue.EnqueueAsync(new GCodeLineCommand("$H"));

        Assert.Equal(new[] { "G1 X1" }, transport.SentLines);

        queue.HandleOk();

        Assert.Equal(new[] { "G1 X1", "$H" }, transport.SentLines);
    }

    [Fact]
    public void Enqueue_NormalCommandAfterExclusiveInFlight_WaitsForExclusiveAck()
    {
        var transport = new FakeDeviceTransport();
        var queue = new BufferAwareCommandQueue(transport);

        _ = queue.EnqueueAsync(new GCodeLineCommand("$H"));
        _ = queue.EnqueueAsync(new GCodeLineCommand("G1 X1"));

        Assert.Equal(new[] { "$H" }, transport.SentLines);

        queue.HandleOk();

        Assert.Equal(new[] { "$H", "G1 X1" }, transport.SentLines);
    }

    [Fact]
    public void UpdateBufferCapacity_IncreasingCapacity_UnblocksPendingCommand()
    {
        var transport = new FakeDeviceTransport();
        var queue = new BufferAwareCommandQueue(transport);
        queue.UpdateBufferCapacity(rxBytesAvailable: 4, plannerBlocksAvailable: 15);

        _ = queue.EnqueueAsync(new GCodeLineCommand("G1 X1"));

        Assert.Empty(transport.SentLines);

        queue.UpdateBufferCapacity(rxBytesAvailable: 20, plannerBlocksAvailable: 15);

        Assert.Equal(new[] { "G1 X1" }, transport.SentLines);
    }
}
