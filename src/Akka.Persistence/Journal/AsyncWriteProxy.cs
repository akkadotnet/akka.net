using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Persistence.Journal
{
    public class AsyncReplayTimeoutException : AkkaException
    {
        public AsyncReplayTimeoutException(string msg)
            : base(msg)
        {
        }
    }

    internal abstract class AsyncWriteProxy : AsyncWriteJournal, IStash
    {
        public void Stash()
        {
            throw new NotImplementedException();
        }

        public void Unstash()
        {
            throw new NotImplementedException();
        }

        public void UnstashAll()
        {
            throw new NotImplementedException();
        }

        public void UnstashAll(Func<Envelope, bool> predicate)
        {
            throw new NotImplementedException();
        }
    }

    internal struct ReplayFailure
    {
        public ReplayFailure(Exception cause) : this()
        {
            Cause = cause;
        }

        public Exception Cause { get; private set; }
    }

    internal class ReplaySuccess
    {
        public static readonly ReplaySuccess Instance = new ReplaySuccess();
        private ReplaySuccess()
        {
        }
    }

    internal class ReplayMediator : ActorBase
    {
        private readonly Action<IPersistentRepresentation> _replayCallback;
        private readonly Task<bool> _replayCompletion;
        private readonly TimeSpan _replayTimeout;

        public ReplayMediator(Action<IPersistentRepresentation> replayCallback, Task<bool> replayCompletion, TimeSpan replayTimeout)
        {
            _replayCallback = replayCallback;
            _replayCompletion = replayCompletion;
            _replayTimeout = replayTimeout;

            Context.SetReceiveTimeout(replayTimeout);
        }

        protected override bool Receive(object message)
        {
            if (message is IPersistentRepresentation)
            {
                _replayCallback(message as IPersistentRepresentation);
            }
            else if (message is ReplaySuccess)
            {
            }
            else if (message is ReplayFailure)
            {

            }
            else if (message is ReceiveTimeout)
            {

            }
            else
            {
                return false;
            }

            return true;
        }
    }
}