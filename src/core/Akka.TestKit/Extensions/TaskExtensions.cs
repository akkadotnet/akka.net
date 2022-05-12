using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using static FluentAssertions.FluentActions;

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
        
        /// <summary>
        /// Guard a <see cref="Task{T}"/> with a timeout and checks to see if
        /// the <see cref="Task{T}.Result"/> matches the provided expected value.
        /// </summary>
        /// <param name="task">The Task to be guarded</param>
        /// <param name="expected">The expected Task.Result</param>
        /// <param name="timeout">The allowed time span for the operation.</param>
        /// <param name="because">
        /// A formatted phrase as is supported by <see cref="M:System.String.Format(System.String,System.Object[])" /> explaining why the assertion
        /// is needed. If the phrase does not start with the word <i>because</i>, it is prepended automatically.
        /// </param>
        /// <param name="becauseArgs">
        /// Zero or more objects to format using the placeholders in <paramref name="because"/>.
        /// </param>
        /// <typeparam name="T"></typeparam>
        public static async Task ShouldCompleteWithin<T>(
            this Task<T> task, T expected, TimeSpan timeout, string because = "", params object[] becauseArgs)
        {
            await Awaiting(async () =>
            {
                var result = await task;
                result.Should().Be(expected);
            }).Should().CompleteWithinAsync(timeout, because, becauseArgs);
        }
        
        /// <summary>
        /// Guard a <see cref="Task{T}"/> with a timeout and returns the <see cref="Task{T}.Result"/>.
        /// </summary>
        /// <param name="task">The Task to be guarded</param>
        /// <param name="timeout">The allowed time span for the operation.</param>
        /// <param name="because">
        /// A formatted phrase as is supported by <see cref="M:System.String.Format(System.String,System.Object[])" /> explaining why the assertion
        /// is needed. If the phrase does not start with the word <i>because</i>, it is prepended automatically.
        /// </param>
        /// <param name="becauseArgs">
        /// Zero or more objects to format using the placeholders in <paramref name="because"/>.
        /// </param>
        /// <typeparam name="T"></typeparam>
        public static async Task<T> ShouldCompleteWithin<T>(
            this Task<T> task, TimeSpan timeout, string because = "", params object[] becauseArgs)
        {
            return (await Awaiting(async () => await task).Should().CompleteWithinAsync(timeout), because, becauseArgs)
                .Item1.Subject;
        }
        
        /// <summary>
        /// Guard a <see cref="Task"/> with a timeout.
        /// </summary>
        /// <param name="task">The Task to be guarded</param>
        /// <param name="timeout">The allowed time span for the operation.</param>
        /// <param name="because">
        /// A formatted phrase as is supported by <see cref="M:System.String.Format(System.String,System.Object[])" /> explaining why the assertion
        /// is needed. If the phrase does not start with the word <i>because</i>, it is prepended automatically.
        /// </param>
        /// <param name="becauseArgs">
        /// Zero or more objects to format using the placeholders in <paramref name="because"/>.
        /// </param>
        public static async Task ShouldCompleteWithin(
            this Task task, TimeSpan timeout, string because = "", params object[] becauseArgs)
        {
            await Awaiting(async () => await task).Should().CompleteWithinAsync(timeout, because, becauseArgs);
        }        
    }
}
