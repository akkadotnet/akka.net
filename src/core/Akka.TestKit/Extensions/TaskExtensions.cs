using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.TestKit.Extensions
{
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
                    }
                    
                    return parentTask.IsCompleted;
                }
                finally
                {
                    cts.Cancel();
                }
            }
        }
        
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
}
