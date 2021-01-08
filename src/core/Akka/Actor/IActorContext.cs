//-----------------------------------------------------------------------
// <copyright file="IActorContext.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Dispatch;

namespace Akka.Actor
{
    /// <summary>
    /// TBD
    /// </summary>
    public interface ICanWatch
    {
        /// <summary>
        /// Monitors the specified actor for termination. When the <paramref name="subject"/> terminates
        /// the instance watching will receive a <see cref="Terminated"/> message.
        /// <remarks>Note that if the <see cref="Terminated"/> message isn't handled by the actor,
        /// by default the actor will crash by throwing a <see cref="DeathPactException"/>. To change
        /// the default behavior, override <see cref="ActorBase.Unhandled"/>.
        /// </remarks>
        /// </summary>
        /// <param name="subject">The actor to monitor for termination.</param>
        /// <returns>Returns the provided subject</returns>
        IActorRef Watch(IActorRef subject);

        /// <summary>
        /// Monitors the specified actor for termination. When the <paramref name="subject"/> terminates
        /// the instance watching will receive the provided message.
        /// </summary>
        /// <param name="subject">The actor to monitor for termination.</param>
        /// <param name="message">The custom termination message</param>
        /// <returns>Returns the provided subject</returns>
        IActorRef WatchWith(IActorRef subject, object message);

        /// <summary>
        /// Stops monitoring the <paramref name="subject"/> for termination.
        /// </summary>
        /// <param name="subject">The actor to stop monitor for termination.</param>
        /// <returns>Returns the provided subject</returns>
        IActorRef Unwatch(IActorRef subject);
    }

    /// <summary>
    /// TBD
    /// </summary>
    public interface IActorContext : IActorRefFactory, ICanWatch
    {
        /// <summary>
        /// Gets the <see cref="IActorRef"/> belonging to the current actor.
        /// </summary>
        IActorRef Self { get; }


        /// <summary>
        /// The <see cref="Props"/> used to originally create this <see cref="IActorRef"/>
        /// </summary>
        Props Props { get; }

        /// <summary>
        /// The dispatcher this actor is running on
        /// </summary>
        MessageDispatcher Dispatcher { get; }

        /// <summary>
        /// Gets the <see cref="IActorRef"/> of the actor who sent the current message.
        /// 
        /// If the message was not sent by an actor (i.e. some external non-actor code
        /// sent this actor a message) then this value will default to <see cref="ActorRefs.NoSender"/>.
        /// </summary>
        IActorRef Sender { get; }

        /// <summary>
        /// Gets a reference to the <see cref="ActorSystem"/> to which this actor belongs.
        /// 
        /// <remarks>
        /// This property is how you can get access to the <see cref="IScheduler"/> and other parts
        /// of Akka.NET from within an actor instance.
        /// </remarks>
        /// </summary>
        ActorSystem System { get; }

        /// <summary>
        /// Gets the <see cref="IActorRef"/> of the parent of the current actor.
        /// </summary>
        IActorRef Parent { get; }

        /// <summary>
        /// Changes the actor's behavior and replaces the current receive handler with the specified handler.
        /// </summary>
        /// <param name="receive">The new message handler.</param>
        void Become(Receive receive);

        /// <summary>
        /// Changes the actor's behavior and replaces the current receive handler with the specified handler.
        /// The current handler is stored on a stack, and you can revert to it by calling <see cref="UnbecomeStacked"/>
        /// <remarks>Please note, that in order to not leak memory, make sure every call to <see cref="BecomeStacked"/>
        /// is matched with a call to <see cref="UnbecomeStacked"/>.</remarks>
        /// </summary>
        /// <param name="receive">The new message handler.</param>
        void BecomeStacked(Receive receive);

        /// <summary>
        /// Changes the actor's behavior and replaces the current receive handler with the previous one on the behavior stack.
        /// <remarks>In order to store an actor on the behavior stack, a call to <see cref="BecomeStacked"/> must have been made
        /// prior to this call</remarks>
        /// </summary>
        void UnbecomeStacked();

        /// <summary>
        /// Retrieves a child actor with the specified name, if it exists.
        /// 
        /// If the child with the given name cannot be found, 
        /// then <see cref="ActorRefs.Nobody"/> will be returned instead.
        /// </summary>
        /// <param name="name">
        /// The name of the child actor.
        /// 
        /// e.g. "child1", "foo"
        /// 
        /// Not the path, just the name of the child at the time it was created by this parent.
        /// </param>
        /// <returns>The <see cref="IActorRef"/> belonging to the child if found, <see cref="ActorRefs.Nobody"/> otherwise.</returns>
        IActorRef Child(string name);

        /// <summary>
        /// Gets all of the children that belong to this actor.
        /// 
        /// If this actor has no children, 
        /// an empty collection of <see cref="IActorRef"/> is returned instead.
        /// </summary>
        /// <returns>TBD</returns>
        IEnumerable<IActorRef> GetChildren();

        /// <summary>
        /// <para>
        /// Defines the inactivity timeout after which the sending of a <see cref="ReceiveTimeout"/> message is triggered.
        /// When specified, the receive function should be able to handle a <see cref="ReceiveTimeout"/> message.
        /// </para>
        /// 
        /// <para>
        /// Please note that the receive timeout might fire and enqueue the <see cref="ReceiveTimeout"/> message right after
        /// another message was enqueued; hence it is not guaranteed that upon reception of the receive
        /// timeout there must have been an idle period beforehand as configured via this method.
        /// </para>
        /// 
        /// <para>
        /// Once set, the receive timeout stays in effect (i.e. continues firing repeatedly after inactivity
        /// periods). Pass in <c>null</c> to switch off this feature.
        /// </para>
        /// </summary>
        /// <param name="timeout">The timeout. Pass in <c>null</c> to switch off this feature.</param>
        void SetReceiveTimeout(TimeSpan? timeout);

        /// <summary>
        /// Gets the inactivity deadline timeout set using <see cref="SetReceiveTimeout"/>.
        /// </summary>
        TimeSpan? ReceiveTimeout { get; }

        /*
  def self: ActorRef
  def props: Props
  def receiveTimeout: Duration  def setReceiveTimeout(timeout: Duration): Unit
  def become(behavior: Actor.Receive, discardOld: Boolean = true): Unit
  def unbecome(): Unit
  def sender: ActorRef
  def children: immutable.Iterable[ActorRef]
  def child(name: String): Option[ActorRef]
  implicit def dispatcher: ExecutionContext
  implicit def system: ActorSystem
  def parent: ActorRef
  def watch(subject: ActorRef): ActorRef
  def unwatch(subject: ActorRef): ActorRef
         */

        /// <summary>
        /// Issues a stop command to the provided <see cref="IActorRef"/>, which will cause that actor
        /// to terminate.
        /// </summary>
        /// <param name="child">The actor who will be stopped.</param>
        void Stop(IActorRef child);
    }
}

