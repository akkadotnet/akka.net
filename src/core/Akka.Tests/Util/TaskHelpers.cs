﻿using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Tests.Util
{
    public static class TaskHelpers
    {
        public static async Task<bool> AwaitWithTimeout(this Task parentTask, TimeSpan timeout)
        {
            using (var cts = new CancellationTokenSource())
            {
                try
                {
                    var delayed = Task.Delay(timeout, cts.Token);
                    var returnedTask = await Task.WhenAny(delayed, parentTask);
                    
                    if(returnedTask == parentTask && returnedTask.Exception != null)
                    {
                        var flattened = returnedTask.Exception.Flatten();
                        ExceptionDispatchInfo.Capture(flattened.InnerException).Throw();
                    }
                    
                    return parentTask.IsCompleted;
                }
                finally
                {
                    cts.Cancel();
                }
            }
        }
        
        public static async Task<T> WithTimeout<T>(this Task<T> parentTask, TimeSpan timeout)
        {
            using (var cts = new CancellationTokenSource())
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
                        ExceptionDispatchInfo.Capture(flattened.InnerException).Throw();
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
}