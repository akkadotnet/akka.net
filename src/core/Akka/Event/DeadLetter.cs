using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    /// Class DeadLetter.
    /// </summary>
    public class DeadLetter
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DeadLetter"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender.</param>
        /// <param name="recipient">The recipient.</param>
        public DeadLetter(object message, IActorRef sender, IActorRef recipient)
        {
            Message = message;
            Sender = sender;
            Recipient = recipient;
        }

        /// <summary>
        /// Gets the message.
        /// </summary>
        /// <value>The message.</value>
        public object Message { get; private set; }

        /// <summary>
        /// Gets the recipient.
        /// </summary>
        /// <value>The recipient.</value>
        public IActorRef Recipient { get; private set; }

        /// <summary>
        /// Gets the sender.
        /// </summary>
        /// <value>The sender.</value>
        public IActorRef Sender { get; private set; }

        public override string ToString()
        {
            return "DeadLetter from " + Sender + " to " + Recipient + ": <" + Message + ">";
        }
    }
}