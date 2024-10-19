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
        SlimResult<T>.ForError(NotYetThereSentinel.Instance);

    private static readonly SlimResult<T> MustNotBeNull =
        SlimResult<T>.ForError(ReactiveStreamsCompliance
            .ElementMustNotBeNullException);
    public static SlimResult<T> FromTask(Task<T> task)
    {
        return task.IsCanceled || task.IsFaulted
            ? SlimResult<T>.ForError(task.Exception)
            : SlimResult<T>.ForSuccess(task.Result);
    }
    public SlimResult(Exception errorOrSentinel, T result)
    {
        if (result == null || errorOrSentinel != null)
        {
            Error = errorOrSentinel ?? ReactiveStreamsCompliance
                .ElementMustNotBeNullException;
        }
        else
        {
            Result = result;
        }
    }

    private SlimResult(Exception errorOrSentinel)
    {
        Error = errorOrSentinel;
        Result = default;
    }

    private SlimResult(T result)
    {
        Error = default;
        Result = result;
    }

    public static SlimResult<T> ForError(Exception errorOrSentinel)
    {
        return new SlimResult<T>(errorOrSentinel);
    }

    public static SlimResult<T> ForSuccess(T result)
    {
        return result == null
            ? SlimResult<T>.MustNotBeNull
            : new SlimResult<T>(result);
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