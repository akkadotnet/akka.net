// -----------------------------------------------------------------------
//  <copyright file="ThreadPoolMonitor.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Persistence.Query.InMemory.Tests.Diagnostics;

/// <summary>
/// Monitors thread pool state at regular intervals and records to DiagnosticTimeline.
/// </summary>
public sealed class ThreadPoolMonitor : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _monitorTask;
    private readonly int _intervalMs;

    /// <summary>
    /// Create and start a thread pool monitor.
    /// </summary>
    /// <param name="intervalMs">Interval between samples in milliseconds</param>
    /// <param name="durationMs">Total duration to monitor in milliseconds</param>
    public ThreadPoolMonitor(int intervalMs = 250, int durationMs = 10000)
    {
        _intervalMs = intervalMs;
        _monitorTask = Task.Run(async () =>
        {
            var iterations = durationMs / intervalMs;
            for (var i = 0; i < iterations && !_cts.Token.IsCancellationRequested; i++)
            {
                RecordThreadPoolState();
                try
                {
                    await Task.Delay(_intervalMs, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        });
    }

    private static void RecordThreadPoolState()
    {
        ThreadPool.GetAvailableThreads(out var availableWorker, out var availableIo);
        ThreadPool.GetMaxThreads(out var maxWorker, out var maxIo);
        ThreadPool.GetMinThreads(out var minWorker, out var minIo);

        var busyWorker = maxWorker - availableWorker;
        var busyIo = maxIo - availableIo;

#if NET6_0_OR_GREATER
        var pendingWorkItems = ThreadPool.PendingWorkItemCount;
        var completedWorkItems = ThreadPool.CompletedWorkItemCount;
        DiagnosticTimeline.Record("ThreadPool",
            $"Workers: {busyWorker} busy / {minWorker} min / {maxWorker} max | " +
            $"IO: {busyIo} busy | " +
            $"Pending: {pendingWorkItems} | " +
            $"Completed: {completedWorkItems}");
#else
        DiagnosticTimeline.Record("ThreadPool",
            $"Workers: {busyWorker} busy / {minWorker} min / {maxWorker} max | " +
            $"IO: {busyIo} busy");
#endif
    }

    /// <summary>
    /// Stop monitoring and wait for the monitor task to complete.
    /// </summary>
    public void Dispose()
    {
        _cts.Cancel();
        _monitorTask.Wait(TimeSpan.FromSeconds(1));
        _cts.Dispose();
    }
}
