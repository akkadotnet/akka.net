//-----------------------------------------------------------------------
// <copyright file="Cell.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor.Internal;
using Akka.Dispatch.SysMsg;

namespace Akka.Actor
{
    /// <summary>
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public interface ICell
    {
        /// <summary>Gets the “self” reference which this Cell is attached to.</summary>
        IActorRef Self { get; }

        /// <summary>The system within which this Cell lives.</summary>
        ActorSystem System { get; }        
        
        /// <summary>The system internals within which this Cell lives.</summary>
        ActorSystemImpl SystemImpl{ get; }

        /// <summary>
        /// Start the cell: enqueued message must not be processed before this has
        /// been called. The usual action is to attach the mailbox to a dispatcher.
        /// </summary>
        void Start();

        /// <summary>Recursively suspend this actor and all its children. Is only allowed to throw fatal exceptions.</summary>
        void Suspend();

        /// <summary>Recursively resume this actor and all its children. Is only allowed to throw fatal exceptions.</summary>
        void Resume(Exception causedByFailure);

        /// <summary>Restart this actor (will recursively restart or stop all children). Is only allowed to throw Fatal Throwables.</summary>
        void Restart(Exception cause);


        /// <summary>Recursively terminate this actor and all its children. Is only allowed to throw Fatal Throwables.</summary>
        void Stop();


        /// <summary>The supervisor of this actor.</summary>
        IInternalActorRef Parent { get; }

        /// <summary>Returns true if the actor is local.</summary>
        bool IsLocal { get; }


        /// <summary>The props for this actor cell.</summary>
        Props Props { get; }

        /// <summary>
        /// If the actor isLocal, returns whether "user messages" are currently queued,
        /// <c>false</c>otherwise.
        /// </summary>
        bool HasMessages { get; }

        /// <summary>
        /// If the actor isLocal, returns the number of "user messages" currently queued,
        /// which may be a costly operation, 0 otherwise.
        /// </summary>
        int NumberOfMessages { get; }

        bool IsTerminated { get; }

        void Post(IActorRef sender, object message);



        IEnumerable<IInternalActorRef> GetChildren();    //TODO: Should be replaced by childrenRefs: ChildrenContainer

        /// <summary>
        /// Method for looking up a single child beneath this actor.
        /// It is racy if called from the outside.</summary>
        IInternalActorRef GetSingleChild(string name);

        IInternalActorRef GetChildByName(string name);

        /// <summary>
        /// Tries to get the stats for the child with the specified name. The stats can be either <see cref="ChildNameReserved"/> 
        /// indicating that only a name has been reserved for the child, or a <see cref="ChildRestartStats"/> for a child that 
        /// has been initialized/created.
        /// </summary>
        bool TryGetChildStatsByName(string name, out IChildStats child); //This is called getChildByName in Akka JVM

        /// <summary>
        /// Enqueue a message to be sent to the actor; may or may not actually
        /// schedule the actor to run, depending on which type of cell it is.
        /// </summary>
        /// <param name="message">The system message we're passing along</param>
        void SendSystemMessage(ISystemMessage message);

        // TODO: Missing:
        //    /**
        //    * The system internals where this Cell lives.
        //    */
        //    def systemImpl: ActorSystemImpl
        //    /**
        //    * All children of this actor, including only reserved-names.
        //    */
        //    def childrenRefs: ChildrenContainer
        //    /**
        //    * Get the stats for the named child, if that exists.
        //    */
        //    def getChildByName(name: String): Option[ChildStats]

        //    /**
        //    * Method for looking up a single child beneath this actor.
        //    * It is racy if called from the outside.
        //    */
        //    def getSingleChild(name: String): InternalActorRef

        //    /**
        //    * Enqueue a message to be sent to the actor; may or may not actually
        //    * schedule the actor to run, depending on which type of cell it is.
        //    * Is only allowed to throw Fatal Throwables.
        //    */
        //    def sendMessage(msg: Envelope): Unit

        //    /**
        //    * Enqueue a message to be sent to the actor; may or may not actually
        //    * schedule the actor to run, depending on which type of cell it is.
        //    * Is only allowed to throw Fatal Throwables.
        //    */
        //    final def sendMessage(message: Any, sender: ActorRef): Unit =
        //    sendMessage(Envelope(message, sender, system))
    }
}

