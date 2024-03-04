// -----------------------------------------------------------------------
//  <copyright file="TaskHelperExts.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Threading.Tasks;

namespace Akka.Streams.Util;

/// <summary>
/// Internal API.
/// Extension Helper Sugar for
/// <see cref="Task{TResult}"/> -> <see cref="ValueTask{TResult}"/> Conversions.
/// </summary>
internal static class TaskHelperExts
{
    public static ValueTask<T> ToValueTask<T>(this Task<T> task)
    {
        return new ValueTask<T>(task);
    }
    
    /// <summary>
    /// Converts a <see cref="Task{Done}"/> into a <see cref="ValueTask"/>
    /// If you want <see cref="ValueTask{Done}"/>,
    /// Call <see cref="ToValueTask{T}"/> with an explicit Type parameter.
    /// </summary>
    public static ValueTask ToValueTask(this Task<Done> task)
    {
        return new ValueTask(task);
    }
}