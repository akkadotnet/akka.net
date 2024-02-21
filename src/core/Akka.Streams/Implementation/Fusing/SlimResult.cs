// //-----------------------------------------------------------------------
// // <copyright file="SlimResult.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;

namespace Akka.Streams.Implementation.Fusing;

public readonly struct SlimResult<T>
{
    public readonly Exception Error;
    public readonly T Result;

    public static readonly SlimResult<T> NotYetReady =
        new SlimResult<T>(NotYetThereSentinel.Instance, default);
        
    public static SlimResult<T> FromTask(Task<T> task)
    {
        return task.IsCanceled || task.IsFaulted
            ? new SlimResult<T>(task.Exception, default)
            : new SlimResult<T>(default, task.Result);
    }
    public SlimResult(Exception errorOrSentinel, T result)
    {
        if (result == null)
        {
            Error = errorOrSentinel ?? ReactiveStreamsCompliance
                .ElementMustNotBeNullException;
        }
        else
        {
            Result = result;
        }
    }

    public bool IsSuccess()
    {
        return Error == null;
    }

    public bool IsDone()
    {
        return Error != NotYetThereSentinel.Instance;
    }
}