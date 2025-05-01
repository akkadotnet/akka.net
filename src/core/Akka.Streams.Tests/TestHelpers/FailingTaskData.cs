// -----------------------------------------------------------------------
//  <copyright file="FailingTaskData.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Streams.Tests.TestHelpers;

internal sealed class FailException() : Exception("Test Exception");

public sealed class FailingTaskData<TIn> : TheoryData<Func<TIn, Task<NotUsed>>>, IDisposable
{
    private readonly CancellationTokenSource _cancelledCts;

    public FailingTaskData()
    {
        _cancelledCts = new CancellationTokenSource();
        _cancelledCts.Cancel();
        
        var cancelledTask = Task.FromCanceled<NotUsed>(_cancelledCts.Token);

        // Immediate return
        Add(_ => Task.FromException<NotUsed>(new FailException()));
        Add(_ =>
        {
            _cancelledCts.Token.ThrowIfCancellationRequested();
            return Task.FromResult(NotUsed.Instance);
        });
        Add(_ => cancelledTask);
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        Add(async _ => throw new FailException());
        Add(async _ => await cancelledTask);
        Add(async _ => cancelledTask.Result);
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        
        // Delayed exception
        Add(async _ =>
        {
            await Task.Delay(100.Milliseconds());
            throw new FailException();
        });
        Add(async _ =>
        {
            await Task.Yield();
            throw new FailException();
        });
        Add(async _ =>
        {
            using var cts = new CancellationTokenSource(100.Milliseconds());
            await Task.Delay(3.Seconds(), cts.Token);
            return NotUsed.Instance;
        });
        Add(async _ =>
        {
            _cancelledCts.Token.ThrowIfCancellationRequested();
            await Task.Yield();
            return NotUsed.Instance;
        });
        
    }

    public void Dispose()
    {
        _cancelledCts.Dispose();
    }
}