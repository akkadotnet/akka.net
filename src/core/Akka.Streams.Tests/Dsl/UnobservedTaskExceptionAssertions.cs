//-----------------------------------------------------------------------
// <copyright file="UnobservedTaskExceptionAssertions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using FluentAssertions;

namespace Akka.Streams.Tests.Dsl
{
    internal static class UnobservedTaskExceptionAssertions
    {
        public static async Task ShouldNotRaiseAsync(Func<Task> repro, Func<AggregateException, bool> matches, string because, Action<AggregateException> onMatch = null)
        {
            var unobserved = new TaskCompletionSource<AggregateException>(TaskCreationOptions.RunContinuationsAsynchronously);

            EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, args) =>
            {
                try
                {
                    var flattened = args.Exception.Flatten();
                    if (matches(flattened))
                    {
                        onMatch?.Invoke(flattened);
                        unobserved.TrySetResult(flattened);
                    }
                }
                finally
                {
                    args.SetObserved();
                }
            };

            TaskScheduler.UnobservedTaskException += handler;
            try
            {
                await repro();

                for (var i = 0; i < 20 && !unobserved.Task.IsCompleted; i++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    await Task.Delay(100);
                }

                unobserved.Task.IsCompleted.Should().BeFalse(because);
            }
            finally
            {
                TaskScheduler.UnobservedTaskException -= handler;
            }
        }
    }
}
