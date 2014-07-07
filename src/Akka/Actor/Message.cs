namespace Akka.Actor
{
    /// <summary>
    ///     Struct Envelope
    /// </summary>
    public struct Envelope
    {
        /// <summary>
        ///     Gets or sets the sender.
        /// </summary>
        /// <value>The sender.</value>
        public ActorRef Sender { get; set; }

        /// <summary>
        ///     Gets or sets the message.
        /// </summary>
        /// <value>The message.</value>
        public object Message { get; set; }

        public override string ToString()
        {
            return "<" + (Message ?? "null") + "> from " + (Sender ?? NoSender.Instance);
        }
    }
}