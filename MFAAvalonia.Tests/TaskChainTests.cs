using MaaFramework.Binding;
using MFAAvalonia.Helper.ValueType;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MFAAvalonia.Tests;

public class TaskChainTests
{
    [Fact]
    public async Task FailedMaaTaskCanContinueWithoutBecomingSuccessful()
    {
        var task = new MFATask
        {
            Type = MFATask.MFATaskType.MAAFW,
            MaaAction = () => Task.FromResult(MaaJobStatus.Failed),
            ContinueOnError = true
        };

        var result = await task.Run(CancellationToken.None);

        Assert.Equal(MFATask.MFATaskStatus.FAILED, result.Status);
        Assert.True(result.ContinueQueue);
    }

    [Fact]
    public async Task ExceptionUsesTheSameContinuePolicy()
    {
        var task = new MFATask
        {
            Type = MFATask.MFATaskType.MAAFW,
            MaaAction = () => throw new InvalidOperationException("test"),
            ContinueOnError = true
        };

        var result = await task.Run(CancellationToken.None);

        Assert.Equal(MFATask.MFATaskStatus.FAILED, result.Status);
        Assert.True(result.ContinueQueue);
    }

    [Fact]
    public void ObservableQueueSupportsConcurrentProducersAndAtomicConsumption()
    {
        const int itemCount = 1_000;
        var queue = new ObservableQueue<int>();

        Parallel.For(0, itemCount, queue.Enqueue);

        var consumed = new HashSet<int>();
        while (queue.TryDequeue(out var item))
            consumed.Add(item);

        Assert.Equal(itemCount, consumed.Count);
        Assert.Equal(0, queue.Count);
        Assert.False(queue.TryDequeue(out _));
    }
}
