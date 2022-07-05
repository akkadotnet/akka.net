// -----------------------------------------------------------------------
//  <copyright file="AsyncJoinState.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Cluster
{
    internal sealed class AsyncJoinState: IDisposable
    {
        private readonly TaskCompletionSource<NotUsed> _completion;
        private CancellationTokenSource _timeoutCts;
        private bool _isDisposed;

        public Task Task => _completion.Task;
        public bool IsCompleted => _completion.Task.IsCompleted; // IsCompleted covers Faulted, Canceled, and RanToCompletion
        
        public AsyncJoinState(
            Cluster cluster, 
            Exception failException,
            Action onComplete, 
            CancellationToken token = default)
        {
            Debug.Assert(cluster != null, $"{nameof(cluster)} != null");
            Debug.Assert(failException != null, $"{nameof(failException)} != null");
            Debug.Assert(onComplete != null, $"{nameof(onComplete)} != null");
            
            _completion = new TaskCompletionSource<NotUsed>(TaskCreationOptions.RunContinuationsAsynchronously);

            _timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _timeoutCts.CancelAfter(cluster.Settings.SeedNodeTimeout);
            _timeoutCts.Token.Register(() =>
            {
                if (IsCompleted)
                    return;
                
                _completion.TrySetException(failException);
                onComplete();
                Dispose();
            });
            
            cluster.RegisterOnMemberUp(() =>
            {
                if (IsCompleted)
                    return;
                
                _completion.TrySetResult(NotUsed.Instance);
                onComplete();
                Dispose();
            });
        }

        public void Dispose()
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