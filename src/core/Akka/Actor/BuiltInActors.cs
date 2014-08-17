using System.Threading;
using Akka.Dispatch.SysMsg;
using Akka.Event;

namespace Akka.Actor
{
    /// <summary>
    ///     Class EventStreamActor.
    /// </summary>
    public class EventStreamActor : ActorBase
    {
        /// <summary>
        ///     Processor for user defined messages.
        /// </summary>
        /// <param name="message">The message.</param>
        protected override bool Receive(object message)
        {
            return true;
        }
    }

    /// <summary>
    ///     Class GuardianActor.
    /// </summary>
    public class GuardianActor : ActorBase
    {
        protected override bool Receive(object message)
        {
            if(message is Terminated)
                Context.Stop(Self);
            else if(message is StopChild)
                Context.Stop(((StopChild)message).Child);
            else
                Context.System.DeadLetters.Tell(new DeadLetter(message,Sender,Self),Sender);
            return true;
        }
    }

    public class SystemGuardianActor : ActorBase
    {
        private readonly ActorRef _userGuardian;

        public SystemGuardianActor(ActorRef userGuardian)
        {
            _userGuardian = userGuardian;
        }

        /// <summary>
        /// Processor for messages that are sent to the root system guardian
        /// </summary>
        /// <param name="message"></param>
        protected override bool Receive(object message)
        {
            //TODO need to add termination hook support
            Context.System.DeadLetters.Tell(new DeadLetter(message, Sender, Self), Sender);
            return true;
        }
    }

    /// <summary>
    ///     Class DeadLetterActorRef.
    /// </summary>
    public class DeadLetterActorRef : EmptyLocalActorRef
    {
        private readonly EventStream _eventStream;

        public DeadLetterActorRef(ActorRefProvider provider, ActorPath path, EventStream eventStream) : base(provider,path,eventStream)
        {
            _eventStream = eventStream;
        }

        protected override void HandleDeadLetter(DeadLetter deadLetter)
        {
            if(!SpecialHandle(deadLetter.Message,deadLetter.Sender))
                _eventStream.Publish(deadLetter);
        }

        protected override bool SpecialHandle(object message, ActorRef sender)
        {
            var w = message as Watch;
            if(w != null)
            {
                if(w.Watchee != this && w.Watcher != this)
                {
                    w.Watcher.Tell(new DeathWatchNotification(w.Watchee, existenceConfirmed: false, addressTerminated: false));
                }
                return true;
            }
            return base.SpecialHandle(message, sender);
        }
    }
}