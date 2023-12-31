using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using Akka.Util;

namespace Akka.Streams.Implementation;

/// <summary>
/// We use this because ValueTask doesn't give us a polite ContinueWith,
/// i.e. one where we can pass state in.
/// </summary>
/// <typeparam name="T"></typeparam>
internal readonly struct ValueTaskCheatingPeeker<T>
{
    internal readonly object? _obj;
    /// <summary>The result to be used if the operation completed successfully synchronously.</summary>
    internal readonly T? _result;
    /// <summary>Opaque value passed through to the <see cref="IValueTaskSource{TResult}"/>.</summary>
    internal readonly short _token;
    /// <summary>true to continue on the captured context; otherwise, false.</summary>
    /// <remarks>Stored in the <see cref="ValueTask{TResult}"/> rather than in the configured awaiter to utilize otherwise padding space.</remarks>
    internal readonly bool _continueOnCapturedContext;
}

internal sealed class
    PooledValueTaskContinuationHelper<T>
{
    private ValueTask<T> _valueTask;
    private readonly Action<Try<T>> _continuationAction;

    // ReSharper disable once StaticMemberInGenericType
    private static readonly Action<object> OnCompletedAction =
        CompletionActionVt;
    
    private static readonly Action<Task<T>,object> TaskCompletedAction = (Task<T> t,object o) =>
    {
        var ca = (Action<Try<T>>)o;
        if (t.IsFaulted)
        {
            var exception = t.Exception?.InnerExceptions != null &&
                            t.Exception.InnerExceptions.Count == 1
                ? t.Exception.InnerExceptions[0]
                : t.Exception;

            ca(new Try<T>(exception));
        }
        else
        {
            ca(new Try<T>(t.Result));
        }
    };

    public PooledValueTaskContinuationHelper(Action<Try<T>> continuationAction)
    {
        _continuationAction = continuationAction; 
    }

    public void AttachAwaiter(ValueTask<T> valueTask)
    {
        _valueTask = valueTask;
        AttachOrContinue();
    }

    private void AttachOrContinue()
    {
        var peeker =
            Unsafe.As<ValueTask<T>, ValueTaskCheatingPeeker<T>>(ref _valueTask);
        if (peeker._obj == null)
        {
            _continuationAction(peeker._result);
        }
        else if (peeker._obj is Task<T> asTask)
        {
            asTask.ContinueWith(TaskCompletedAction,_continuationAction,
                TaskContinuationOptions.NotOnCanceled);
        }
        else
        {
            var source = Unsafe.As<IValueTaskSource<T>>(peeker._obj);
            source.OnCompleted(OnCompletedAction, this, peeker._token,
                ValueTaskSourceOnCompletedFlags.None);
        }
    }

    //TODO: May be better to have instanced and take the alloc penalty,
    //      To avoid casting cost here.
    private static void CompletionActionVt(object discard)
    {
        var inst = (PooledValueTaskContinuationHelper<T>)discard;
        var vtCapture = inst._valueTask;
        inst._valueTask = default;
        if (vtCapture.IsCompletedSuccessfully)
        {
            var result = vtCapture.Result;
            inst._continuationAction(result);
        }
        else if(vtCapture.IsCanceled == false)
        {
            inst.VTCompletionError(vtCapture);
        }
    }

    private void VTCompletionError(ValueTask<T> vtCapture)
    {
        var t = vtCapture.AsTask();
        //We only care about faulted, not canceled.
        if (t.IsFaulted)
        {
            var exception = t.Exception?.InnerExceptions != null &&
                            t.Exception.InnerExceptions.Count == 1
                ? t.Exception.InnerExceptions[0]
                : t.Exception;

            _continuationAction(new Try<T>(exception));
        }
    }
}