//-----------------------------------------------------------------------
// <copyright file="TaskEx.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;

namespace Akka.Util.Internal
{
    /// <summary>
    /// INTERNAL API.
    /// 
    /// Renamed from <see cref="Akka.Util.Internal.TaskExtensions"/> so it doesn't collide
    /// with a helper class in the same namespace defined in System.Threading.Tasks.
    /// </summary>
    internal static class TaskEx
    {
        private const int RunContinuationsAsynchronously = 64;
        public static readonly bool IsRunContinuationsAsynchronouslyAvailable = Enum.IsDefined(typeof(TaskCreationOptions), RunContinuationsAsynchronously);

        /// <summary>
        /// Creates a new <see cref="TaskCompletionSource{TResult}"/> which will run in asynchronous,
        /// non-blocking fashion upon calling <see cref="TaskCompletionSource{TResult}.TrySetResult"/>.
        ///
        /// This behavior is not available on all supported versions of .NET framework, in this case it
        /// should be used only together with <see cref="NonBlockingTrySetResult{T}"/> and
        /// <see cref="NonBlockingTrySetException{T}"/>.
        /// </summary>
        public static TaskCompletionSource<T> NonBlockingTaskCompletionSource<T>()
        {
            if (IsRunContinuationsAsynchronouslyAvailable)
            {
                return new TaskCompletionSource<T>((TaskCreationOptions)RunContinuationsAsynchronously);
            }
            else
            {
                return new TaskCompletionSource<T>();
            }
        }

        /// <summary>
        /// Tries to complete given <paramref name="taskCompletionSource"/> in asynchronous, non-blocking
        /// fashion. For safety reasons, this method should be called only on tasks created via
        /// <see cref="NonBlockingTaskCompletionSource{T}"/> method.
        /// </summary>
        public static void NonBlockingTrySetResult<T>(this TaskCompletionSource<T> taskCompletionSource, T value)
        {
            if (IsRunContinuationsAsynchronouslyAvailable)
                taskCompletionSource.TrySetResult(value);
            else
                Task.Run(() => taskCompletionSource.TrySetResult(value));
        }

        /// <summary>
        /// Tries to set <paramref name="exception"/> given <paramref name="taskCompletionSource"/>
        /// in asynchronous, non-blocking fashion. For safety reasons, this method should be called only
        /// on tasks created via <see cref="NonBlockingTaskCompletionSource{T}"/> method.
        /// </summary>
        public static void NonBlockingTrySetException<T>(this TaskCompletionSource<T> taskCompletionSource, Exception exception)
        {
            if (IsRunContinuationsAsynchronouslyAvailable)
                taskCompletionSource.TrySetException(exception);
            else
                Task.Run(() => taskCompletionSource.TrySetException(exception));
        }

        /// <summary>
        /// A completed task
        /// </summary>
        public static readonly Task<Done> Completed = Task.FromResult(Done.Instance);

        /// <summary>
        /// Creates a failed <see cref="Task"/>
        /// </summary>
        /// <param name="ex">The exception to use to fail the task.</param>
        /// <returns>A failed task.</returns>
        public static Task FromException(Exception ex)
        {
            var c = new TaskCompletionSource<Done>();
            c.SetException(ex);
            return c.Task;
        }

        /// <summary>
        /// Creates a failed <see cref="Task"/>
        /// </summary>
        /// <param name="ex">The exception to use to fail the task.</param>
        /// <returns>A failed task.</returns>
        /// <typeparam name="T">The type of <see cref="Task{T}"/></typeparam>
        public static Task<T> FromException<T>(Exception ex)
        {
            var c = new TaskCompletionSource<T>();
            c.SetException(ex);
            return c.Task;
        }
    }
}
