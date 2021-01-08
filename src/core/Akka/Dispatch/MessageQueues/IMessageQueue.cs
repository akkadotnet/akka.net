//-----------------------------------------------------------------------
// <copyright file="IMessageQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;

namespace Akka.Dispatch.MessageQueues
{
    /// <summary> 
    /// Interface to be implemented by all mailbox message queues 
    /// </summary>
    public interface IMessageQueue
    {
        /// <summary> 
        /// Tests if the message queue contains any messages 
        /// </summary>
        bool HasMessages { get; }

        /// <summary> 
        /// Returns the count of messages currently in the message queue 
        /// </summary>
        int Count { get; }

        /// <summary> 
        /// Enqueues an mailbox envelope onto the message queue 
        /// </summary>
        /// <param name="receiver">
        /// The receiver of the messages.
        /// 
        /// This field is only used in a couple of places, but it should not be removed.
        /// </param>
        /// <param name="envelope"> The envelope to enqueue </param>
        void Enqueue(IActorRef receiver, Envelope envelope);

        /// <summary> 
        /// Tries to pull an envelope of the message queue 
        /// </summary>
        /// <param name="envelope"> The envelope that was dequeued </param>
        /// <returns><c>true</c> if there's a message in the queue. <c>false</c> otherwise.</returns>
        bool TryDequeue(out Envelope envelope);

        /// <summary>
        /// Called when the <see cref="Mailbox"/> this queue belongs to is disposed of. Normally
        /// it is expected to transfer all remaining messages into the deadletter queue which is passed in. The owner
        /// of this <see cref="IMessageQueue"/> is passed in if available (e.g. for creating <see cref="DeadLetter"/>s),
        /// "/deadletters" otherwise.
        /// </summary>
        /// <param name="owner">The owner of this message queue if available, "/deadletters" otherwise.</param>
        /// <param name="deadletters">The dead letters message queue.</param>
        void CleanUp(IActorRef owner, IMessageQueue deadletters);
    }
}

