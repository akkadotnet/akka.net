// //-----------------------------------------------------------------------
// // <copyright file="UnsafeSlimHolder.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace Akka.Streams.Implementation;

/// <summary>
/// Used for Pooling ValueTasks by <see cref="PooledAwaitOutGraphStageLogic{T}"/>
/// </summary>
/// <remarks>
/// This is intentionally only guarded for the sake of avoiding Exceptions,
/// As it is expected that implementations are within Akka Streams Stages
/// (which have their own safety guarantees) and geared towards performance.
/// </remarks>
internal class UnsafeSlimHolder<T>
{
    private readonly PooledAwaitOutGraphStageLogic<T> _parent;
    private ValueTask<T> _vt;
    private readonly Action _continuation; 
    public UnsafeSlimHolder(PooledAwaitOutGraphStageLogic<T> logic)
    {
        _parent = logic;
        _continuation = ContinuationAction;
    }

    public void SetContinuation(ValueTask<T> vt, bool configureAwait = true)
    {
        if (configureAwait)
        {
            _vt = vt;
        }
        else
        {
            var whyMSFTwhy = _vt.ConfigureAwait(false);
            _vt = Unsafe.As<ConfiguredValueTaskAwaitable<T>, ValueTask<T>>(
                ref whyMSFTwhy);
        }
        _vt.GetAwaiter().OnCompleted(_continuation);
    }
    private void ContinuationAction()
    {
        var vt = _vt;
        _vt = default;
        _parent.RunIfSame(this, vt);
    }
}