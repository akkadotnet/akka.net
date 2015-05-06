using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor.Internal;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;

namespace Akka.Actor.Await
{
    public class ActorAwareTaskAwaiterBase : ICriticalNotifyCompletion, INotifyCompletion
    {
        private readonly Task _task;
        private readonly AsyncBehavior _behavior;
        private readonly AmbientState _state;
        private readonly ActorCell _context;

        public ActorAwareTaskAwaiterBase(Task task, AsyncBehavior behavior)
        {
            _task = task;
            _behavior = behavior;
            _context = ActorCell.Current;
            _state = new AmbientState
            {
                Sender = _context.Sender,
                Self = _context.Self,
                Message = _context.CurrentMessage
            };
        }

        public bool IsCompleted
        {
            get { return _task.IsCompleted; }
        }

        class SuspendState
        {
            public int ReentrancySuspensionDepth;
        }

        [ThreadStatic]
        private static SuspendState _tlsSuspendState;

        public void OnCompleted(Action continuation)
        {
            var state = _tlsSuspendState ?? new SuspendState();
            if (_behavior == AsyncBehavior.Suspend)
            {
                state.ReentrancySuspensionDepth++;
                if (state.ReentrancySuspensionDepth == 1)
                    _context.SuspendReentrancy();
            }
            _task.ContinueWith(t =>
            {
                Action callback = () =>
                {
                    _tlsSuspendState = state;
                    try
                    {
                        continuation();
                    }
                    finally
                    {
                        _tlsSuspendState = null;
                        if (_behavior == AsyncBehavior.Suspend)
                        {
                            state.ReentrancySuspensionDepth--;
                            if (state.ReentrancySuspensionDepth == 0)
                                _context.ResumeReentrancy();
                        }
                    }
                };
                var message = _behavior == AsyncBehavior.Reentrant
                    ? (object) new CompleteReentrantAwaitedTask(_state, callback)
                    : new CompleteTask(_state, callback);
                _context.Self.Tell(message, _state.Sender);

            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        public void UnsafeOnCompleted(Action continuation)
        {
            OnCompleted(continuation);
        }
    }

    public class ActorAwareTaskAwaiter : ActorAwareTaskAwaiterBase
    {
        private readonly Task _task;

        public void GetResult()
        {
            _task.GetAwaiter().GetResult();
        }

        public ActorAwareTaskAwaiter(Task task, AsyncBehavior behavior) : base(task, behavior)
        {
            _task = task;
        }
    }

    public class ActorAwareTaskAwaiter<TResult> : ActorAwareTaskAwaiterBase
    {
        private readonly Task<TResult> _task;

        public ActorAwareTaskAwaiter(Task<TResult> task, AsyncBehavior behavior) : base(task, behavior)
        {
            _task = task;
        }

        public TResult GetResult()
        {
            return _task.GetAwaiter().GetResult();
        }
    }
}
