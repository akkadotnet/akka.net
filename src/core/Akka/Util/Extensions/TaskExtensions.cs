//-----------------------------------------------------------------------
// <copyright file="TaskExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace System.Threading.Tasks
{
    internal static class TaskExtensions
    {
#if NETSTANDARD
        public static async Task WaitAsync(this Task task, TimeSpan timeout)
        {
            var cts = new CancellationTokenSource();
            try
            {
                var delayTask = Task.Delay(timeout, cts.Token);
                var completedTask = await Task.WhenAny(task, delayTask);
                if (completedTask == delayTask)
                    throw new TimeoutException($"Execution did not complete within the time allotted {timeout.TotalMilliseconds} ms");

                await task;
            }
            finally
            {
                cts.Cancel();
                cts.Dispose();
            }
        }

        public static async Task<TResult> WaitAsync<TResult>(this Task<TResult> task, TimeSpan timeout)
        {
            var cts = new CancellationTokenSource();
            try
            {
                var delayTask = Task.Delay(timeout, cts.Token);
                var completedTask = await Task.WhenAny(task, delayTask);
                return completedTask == delayTask
                    ? throw new TimeoutException($"Execution did not complete within the time allotted {timeout.TotalMilliseconds} ms")
                    : await task;
            }
            finally
            {
                cts.Cancel();
                cts.Dispose();
            }
        }
#endif
    }
}
