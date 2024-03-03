// -----------------------------------------------------------------------
//  <copyright file="TaskHelperExts.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Threading.Tasks;

namespace Akka.Streams.Tests.Util;

public static class TaskHelperExts
{
    public static ValueTask<T> ToValueTask<T>(this Task<T> task)
    {
        return new ValueTask<T>(task);
    }

    public static ValueTask ToValueTask(this Task<Done> task)
    {
        return new ValueTask(task);
    }
}