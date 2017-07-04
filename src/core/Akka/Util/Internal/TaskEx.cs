//-----------------------------------------------------------------------
// <copyright file="TaskEx.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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