using System;
using System.Threading;


namespace Akka.Actor
{
    /// <summary>
    /// Schedule execution of "await" continuation into the mailbox
    /// </summary>
    class ActorSynchronizationContext : SynchronizationContext
    {
        private readonly ActorCell _cell;
        private readonly IActorRef _sender;

        public ActorSynchronizationContext(ActorCell cell)
        {
            _cell = cell;
            _sender = cell.Sender;
        }

        public override void Post(SendOrPostCallback d, object state)
        {
            if(_cell.IsTerminated)
                return;

            var envelope = new Envelope 
            { 
                Message = new AsyncContinuation(d, state), 
                Sender = _sender
            };
            
            _cell.Mailbox.Post(_cell.Self, envelope);
        }
    }

    public class AsyncContinuation : IAutoReceivedMessage
    {
        public readonly SendOrPostCallback Continuation;
        public readonly object State;

        public AsyncContinuation(SendOrPostCallback continuation, object state)
        {
            Continuation = continuation;
            State = state;
        }
    }
}
