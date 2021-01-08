//-----------------------------------------------------------------------
// <copyright file="NullMessageEnvelope.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.TestKit
{
    /// <summary>
    /// TBD
    /// </summary>
    public sealed class NullMessageEnvelope : MessageEnvelope
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static NullMessageEnvelope Instance=new NullMessageEnvelope();

        private NullMessageEnvelope(){}

        /// <summary>
        /// N/A
        /// </summary>
        /// <exception cref="IllegalActorStateException">
        /// This exception is thrown automatically since this envelope does not contain a message.
        /// </exception>
        public override object Message
        {
            get { throw new IllegalActorStateException("last receive did not dequeue a message"); }
        }

        /// <summary>
        /// N/A
        /// </summary>
        /// <exception cref="IllegalActorStateException">
        /// This exception is thrown automatically since this envelope does not have a sender.
        /// </exception>
        public override IActorRef Sender
        {
            get { throw new IllegalActorStateException("last receive did not dequeue a message"); }
        }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            return "<null>";
        }
    }
}
