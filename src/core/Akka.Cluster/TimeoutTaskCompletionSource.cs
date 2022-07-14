// -----------------------------------------------------------------------
//  <copyright file="AsyncJoinState.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Cluster
{
    internal sealed class TimeoutTaskCompletionSource
    {
        private readonly TaskCompletionSource<NotUsed> _completion;
        private readonly Exception _failException;
        private CancellationTokenSource _timeoutCts;
        private bool _isDisposed;

        public Task Task => _completion.Task;
        public bool IsCompleted => _completion.Task.IsCompleted; // IsCompleted covers Faulted, Canceled, and RanToCompletion
        
        public TimeoutTaskCompletionSource(
            TimeSpan timeout, 
            Exception failException,
            CancellationToken token = default)
        {
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentException("Must be greater than zero", nameof(timeout));
            if (failException == null)
                throw new ArgumentException("Must not be null", nameof(failException));
            
            _completion = new TaskCompletionSource<NotUsed>(TaskCreationOptions.RunContinuationsAsynchronously);
            _failException = failException;

            _timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _timeoutCts.CancelAfter(timeout);
            _timeoutCts.Token.Register(Fail);
        }

        public void Complete()
        {
            if (IsCompleted)
                return;
                
            _completion.TrySetResult(NotUsed.Instance);
            Dispose();
        }

        private void Fail()
        {
            if (IsCompleted)
                return;
                
            _completion.TrySetException(_failException);
            Dispose();
        }
        
        // Dispose has to be called when the task is either cancelled, timed out, or completed.
        private void Dispose()
        {
            if(!_isDisposed)
            {
                //Clean up managed resources
                _timeoutCts?.Dispose();
                //Clean up unmanaged resources
                _timeoutCts = null;
            }
            _isDisposed = true;
        }
    }
}