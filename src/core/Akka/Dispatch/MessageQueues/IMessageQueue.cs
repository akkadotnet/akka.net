﻿//-----------------------------------------------------------------------
// <copyright file="IMessageQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

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
        /// <param name="envelope"> The envelope to enqueue </param>
        void Enqueue(Envelope envelope);

        /// <summary> 
        /// Tries to pull an envelope of the message queue 
        /// </summary>
        /// <param name="envelope"> The envelope that was dequeued </param>
        /// <returns> </returns>
        bool TryDequeue(out Envelope envelope);
    }
}

