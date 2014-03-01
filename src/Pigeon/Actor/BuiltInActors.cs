using Akka.Event;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Actor
{
    /// <summary>
    /// Class EventStreamActor.
    /// </summary>
    public class EventStreamActor : UntypedActor
    {
        /// <summary>
        /// Processor for user defined messages.
        /// </summary>
        /// <param name="message">The message.</param>
        protected override void OnReceive(object message)
        {
        }
    }

    /// <summary>
    /// Class GuardianActor.
    /// </summary>
    public class GuardianActor : UntypedActor
    {
        /// <summary>
        /// Processor for user defined messages.
        /// </summary>
        /// <param name="message">The message.</param>
        protected override void OnReceive(object message)
        {
            Unhandled(message);
        }
    }

    /// <summary>
    /// Class DeadLetterActorRef.
    /// </summary>
    public class DeadLetterActorRef : ActorRef
    {
        /// <summary>
        /// The event stream
        /// </summary>
        private EventStream eventStream;
        /// <summary>
        /// The path
        /// </summary>
        private ActorPath path;
        /// <summary>
        /// The provider
        /// </summary>
        private ActorRefProvider provider;
        /// <summary>
        /// Initializes a new instance of the <see cref="DeadLetterActorRef"/> class.
        /// </summary>
        /// <param name="provider">The provider.</param>
        /// <param name="path">The path.</param>
        /// <param name="eventStream">The event stream.</param>
        public DeadLetterActorRef(ActorRefProvider provider, ActorPath path, EventStream eventStream)
        {
            this.eventStream = eventStream;
            this.path = path;
            this.provider = provider; 
        }
        /// <summary>
        /// Specials the handle.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        private bool SpecialHandle(object message, ActorRef sender)
        {
            return false;
        }

        /// <summary>
        /// Tells the internal.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender.</param>
        protected override void TellInternal(object message, ActorRef sender)
        {
            message
                .Match()
                .With<Identify>(m => sender.Tell(new ActorIdentity(m.MessageId, this)))
                .With<DeadLetter>(m =>
                {
                    if (!SpecialHandle(m.Message, m.Sender))
                        eventStream.Publish(m);
                })
                .Default(m =>
                {
                    if (!SpecialHandle(message, sender))
                        eventStream.Publish(new DeadLetter(message, sender == ActorRef.NoSender ? provider.DeadLetters : sender, this));
                });
        }        
    }
}
