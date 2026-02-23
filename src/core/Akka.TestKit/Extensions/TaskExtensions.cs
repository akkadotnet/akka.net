//-----------------------------------------------------------------------
// <copyright file="TaskExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

#nullable enable
namespace Akka.TestKit.Extensions;

public static class TaskExtensions
{
    public static async Task<bool> AwaitWithTimeout(this Task parentTask, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            try
            {
                var delayed = Task.Delay(timeout, cts.Token);
                var returnedTask = await Task.WhenAny(delayed, parentTask);
                    
                if(returnedTask == parentTask && returnedTask.Exception != null)
                {
                    var flattened = returnedTask.Exception.Flatten();
                    if(flattened.InnerExceptions.Count == 1)
                        ExceptionDispatchInfo.Capture(flattened.InnerExceptions[0]).Throw();
                    else
                        ExceptionDispatchInfo.Capture(returnedTask.Exception).Throw();
                    return false;
                }
                    
                return parentTask.IsCompleted;
            }
            finally
            {
                cts.Cancel();
            }
        }
    }
        
    // TODO: replace with WaitAsync after we drop .NET Standard 2.0 support
    public static async Task<T> WithTimeout<T>(this Task<T> parentTask, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            try
            {
                var delayed = Task.Delay(timeout, cts.Token);
                var returnedTask = await Task.WhenAny(delayed, parentTask);

                if (returnedTask != parentTask)
                    throw new TaskCanceledException($"Task timed out after {timeout.TotalSeconds} seconds");
                    
                if(returnedTask == parentTask && returnedTask.Exception != null)
                {
                    var flattened = returnedTask.Exception.Flatten();
                    if(flattened.InnerExceptions.Count == 1)
                        ExceptionDispatchInfo.Capture(flattened.InnerExceptions[0]).Throw();
                    else
                        ExceptionDispatchInfo.Capture(returnedTask.Exception).Throw();
                }
                    
                return parentTask.Result;
            }
            finally
            {
                cts.Cancel();
            }
        }
    }
}