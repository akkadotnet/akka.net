// //-----------------------------------------------------------------------
// // <copyright file="PooledAwaitOutGraphStageLogic.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;

namespace Akka.Streams.Implementation;

/// <summary>
/// Semi-unsafe Helper intermediate for <see cref="OutGraphStageLogic"/>
/// that allows for a ValueTask Wait to be pooled.
/// <para/>
/// Inheritors are expected to utilize the <see cref="SetPooledCompletionCallback"/>
/// and call `base.PreStart()` in their `PreStart` conditions.
/// <para/>
/// Additionally, if inheritors have their own 'restart' logic,
/// They should utilize the `ResetHolder()` method,
/// to avoid callback clashes.
///  
/// </summary>
/// <typeparam name="T"></typeparam>
internal abstract class PooledAwaitOutGraphStageLogic<T> : OutGraphStageLogic
{
    private UnsafeSlimHolder<T> _unsafeSlimHolder;
    protected Action<SlimResult<T>> _completedCallback;
    protected PooledAwaitOutGraphStageLogic(Shape shape) : base(shape)
    {
        _completedCallback = GetAsyncCallback<SlimResult<T>>(t =>
        {
            FailStage(new Exception("Callback was not set!"));
        });
    }

    protected void SetPooledCompletionCallback(Action<SlimResult<T>> completedCallback)
    {
        if (_completedCallback == null)
        {
            throw new ArgumentNullException(
                nameof(completedCallback));
        }
        _completedCallback = GetAsyncCallback(completedCallback);
    }

    public override void PreStart()
    {
        ResetHolder();
    }
        
    /// <summary>
    /// Sets a ValueTask to wire up the callback,
    /// set via <see cref="SetPooledCompletionCallback"/>.
    /// If <see cref="SetPooledCompletionCallback"/> has not been called,
    /// The continuation will fail the stage!
    /// </summary>
    /// <param name="valueTask"></param>
    protected void SetContinuation(ValueTask<T> valueTask, bool configureAwait = true)
    {
        _unsafeSlimHolder.SetContinuation(valueTask, configureAwait);
    }

    /// <summary>
    /// Use at own risk!
    /// </summary>
    protected void SetHolder(UnsafeSlimHolder<T> holder)
    {
        Interlocked.Exchange(ref _unsafeSlimHolder, holder);
    }

    public void ResetHolder()
    {
        Interlocked.Exchange(ref _unsafeSlimHolder, new UnsafeSlimHolder<T>(this));
    }

    internal void RunIfSame(UnsafeSlimHolder<T> unsafeSlimHolder, ValueTask<T> vt)
    {
        //We are explicitly using referenceEquals here,
        //since we are explicitly resetting things.
        if (object.ReferenceEquals(_unsafeSlimHolder, unsafeSlimHolder))
        {
            _completedCallback(vt.IsCompletedSuccessfully
                ? SlimResult<T>.ForSuccess(vt.Result)
                : SlimResult<T>.FromTask(vt.AsTask()));
        }
    }
}