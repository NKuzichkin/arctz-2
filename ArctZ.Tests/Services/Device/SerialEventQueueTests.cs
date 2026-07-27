using System;
using System.Collections.Generic;
using ArctZ.Services.Device;

namespace ArctZ.Tests.Services.Device;

public class SerialEventQueueTests
{
    [Fact]
    public void Enqueue_ActionThrows_DoesNotPropagateAndStillRunsLaterActions()
    {
        var queue = new SerialEventQueue();
        var ran = new List<string>();
        var caught = new List<Exception>();
        queue.UnhandledException += caught.Add;

        queue.Enqueue(() =>
        {
            ran.Add("outer");

            // Re-entrant enqueues are drained by the outer call, so both of these
            // run inside the same drain loop as the throwing action.
            queue.Enqueue(() => throw new InvalidOperationException("boom"));
            queue.Enqueue(() => ran.Add("after-throw"));
        });

        queue.Enqueue(() => ran.Add("later"));

        Assert.Equal(new[] { "outer", "after-throw", "later" }, ran);
        Assert.Single(caught);
        Assert.IsType<InvalidOperationException>(caught[0]);
    }
}
